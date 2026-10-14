using IFB.Client.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Path = System.IO.Path;
using Windows.System;

namespace IFB.Client.Views.Pages;

public sealed partial class ChatPage : Page
{
    private readonly ChatViewModel _vm;
    private Storyboard? _typingDotStoryboard;
    private string? _conversationId;
    private System.ComponentModel.PropertyChangedEventHandler? _propertyHandler;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionHandler;
    private bool _stickToBottom = true;

    private void ScrollToNewest()
    {
        MessagesScrollViewer.UpdateLayout();
        MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null, true);
    }

    public ChatPage()
    {
        InitializeComponent();
        _vm = App.Services.GetRequiredService<ChatViewModel>();
        DataContext = _vm;
        Services.FluidMotion.Attach(SendButton);
        MessagesList.ItemsSource = _vm.Messages;
        // Stay pinned to the newest message while the reader is at the bottom, including when
        // image previews finish loading and make earlier bubbles taller.
        MessagesScrollViewer.ViewChanged += (_, _) =>
            _stickToBottom = MessagesScrollViewer.VerticalOffset >= MessagesScrollViewer.ScrollableHeight - 48;
        MessagesList.SizeChanged += (_, _) => { if (_stickToBottom) ScrollToNewest(); };
        ActualThemeChanged += (_, _) => { foreach (var message in _vm.Messages) message.RefreshAppearance(); };

        // Observe typing indicator visibility to control animation
        _propertyHandler = (_, e) =>
        {
            TypingBar.Visibility = _vm.IsTypingVisible ? Visibility.Visible : Visibility.Collapsed;
            TypingLabel.Text = _vm.TypingIndicatorText ?? "";
            ReplyBar.Visibility = _vm.ReplyTarget == null ? Visibility.Collapsed : Visibility.Visible;
            ReplyToName.Text = _vm.ReplyTarget?.SenderDisplayName ?? ""; ReplyToPreview.Text = _vm.ReplyTarget?.Content ?? "";
            if (e.PropertyName == nameof(ChatViewModel.IsTypingVisible))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (_vm.IsTypingVisible && Services.ThemeTokens.AnimationsEnabled) _typingDotStoryboard?.Begin();
                    else _typingDotStoryboard?.Stop();
                });
            }
        };

        // Auto-scroll to bottom when new messages arrive
        _collectionHandler = (_, e) =>
        {
            // Only appended messages move the view; "Earlier" inserts older messages above it.
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewStartingIndex < _vm.Messages.Count - 1) return;
            _stickToBottom = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, ScrollToNewest);
        };

        StartTypingDotAnimation();
    }

    protected override async void OnNavigatedTo(
        Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm.PropertyChanged += _propertyHandler; _vm.Messages.CollectionChanged += _collectionHandler;

        if (e.Parameter is string conversationId)
        {
            _conversationId = conversationId;
            // Load real messages from server
            try { await _vm.LoadConversationAsync(conversationId); } catch (Exception ex) { await Ui.Error(this, ex); }

            // Update header from parent MainViewModel
            if (App.Current.MainWindow?.DataContext is MainViewModel main)
            {
                var conv = main.Conversations
                    .FirstOrDefault(c => c.Id == conversationId);
                if (conv != null)
                {
                    ConversationTitle.Text = conv.Name;
                    HeaderAvatar.DisplayName = conv.Name;
                    HeaderAvatar.PresenceStatus = conv.Presence;
                    ConversationSubtitle.Text =
                        $"{conv.Members.Count} members";
                }
            }
        }
    }

    protected override void OnNavigatedFrom(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    { _vm.PropertyChanged -= _propertyHandler; _vm.Messages.CollectionChanged -= _collectionHandler; _typingDotStoryboard?.Stop(); base.OnNavigatedFrom(e); }

    // ── Input handlers ────────────────────────────────────────────────────────

    private void MessageInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_vm == null) return;
        _vm.ComposerText = MessageInput.Text;
        SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageInput.Text);

        // Notify typing if more than 2 characters have been typed
        if (MessageInput.Text.Length > 1)
            _ = _vm.NotifyTypingAsync();
    }

    private void MessageInput_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Enter sends, Shift+Enter newline
        if (e.Key == VirtualKey.Enter &&
            !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down))
        {
            e.Handled = true;
            _ = SendMessageAsync();
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e) =>
        await SendMessageAsync();

    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(MessageInput.Text)) return;
        SendButton.IsEnabled = false;
        try { await _vm.SendMessageCommand.ExecuteAsync(null); MessageInput.Text = _vm.ComposerText; }
        catch (Exception ex) { await Ui.Error(this, ex); }
        finally { SendButton.IsEnabled = !string.IsNullOrWhiteSpace(MessageInput.Text); }
    }

    private async void Attach_Click(object sender, RoutedEventArgs e)
    {
        if (_conversationId == null) return;
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.Current.MainWindow!));
            var selected = await picker.PickSingleFileAsync(); if (selected == null) return;
            var uploaded = await App.Services.GetRequiredService<Services.ClientRequests>().UploadAsync(_conversationId, selected.Path);
            await App.Services.GetRequiredService<Services.IHubConnectionService>().SendMessageAsync(new IFB.Shared.Contracts.SendMessageDto(
                _conversationId, uploaded.FileName, IFB.Shared.Enums.MessageType.File, _vm.ReplyTarget?.Id, uploaded.Id));
        }
        catch (Exception ex) { await Ui.Error(this, ex); }
    }

    private void CancelReply_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearReplyCommand.Execute(null);
        ReplyBar.Visibility = Visibility.Collapsed;
    }

    private async void ShowDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_conversationId != null) try { await App.Current.MainWindow!.ManageMembersAsync(_conversationId); } catch (Exception ex) { await Ui.Error(this, ex); }
    }

    private void Message_RightTapped(object sender, RightTappedRoutedEventArgs e)
    { ShowMessageActions(sender); e.Handled = true; }

    private void MessageActions_Click(object sender, RoutedEventArgs e) => ShowMessageActions(sender);

    private void ShowMessageActions(object sender)
    {
        if (sender is not FrameworkElement { DataContext: MessageItemViewModel message } element) return;
        var menu = new MenuFlyout();
        void Action(string label, Func<Task> action) { var item = new MenuFlyoutItem { Text = label }; item.Click += async (_, _) => { try { await action(); } catch (Exception ex) { await Ui.Error(this, ex); } }; menu.Items.Add(item); }
        Action("Reply", () => { _vm.SetReplyTargetCommand.Execute(message); MessageInput.Focus(FocusState.Programmatic); return Task.CompletedTask; });
        if (message.IsFromCurrentUser && !message.IsDeleted)
        {
            Action("Edit", async () => { var input = new TextBox { Text = message.Content, MaxLength = 10000, AcceptsReturn = true }; var dialog = new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "Edit message", Content = input, PrimaryButtonText = "Save", CloseButtonText = "Cancel" };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary) await App.Services.GetRequiredService<Services.IHubConnectionService>().EditMessageAsync(new IFB.Shared.Contracts.EditMessageDto(message.Id, input.Text)); });
            Action("Delete", () => App.Services.GetRequiredService<Services.IHubConnectionService>().DeleteMessageAsync(new IFB.Shared.Contracts.DeleteMessageDto(message.Id)));
        }
        foreach (var emoji in Enum.GetValues<IFB.Shared.Enums.ReactionEmoji>())
            Action($"{MessageItemViewModel.ReactionGlyph(emoji)}  {emoji}", () => App.Services.GetRequiredService<Services.IHubConnectionService>().ToggleReactionAsync(new IFB.Shared.Contracts.ToggleReactionDto(message.Id, (int)emoji)));
        menu.ShowAt(element);
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MessageItemViewModel { FileId: not null } message }) return;
        string? previewPath = null;
        try
        {
            var api = App.Services.GetRequiredService<Services.ClientRequests>();
            var file = await api.GetAsync<Services.SharedFile>($"api/v1/files/{message.FileId}/info");
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" }.Contains(extension))
                throw new InvalidOperationException("This attachment is not a supported image. Use Download attachment to save it.");
            previewPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
            await api.DownloadAsync(file, previewPath);
            var storage = await Windows.Storage.StorageFile.GetFileFromPathAsync(previewPath);
            using var stream = await storage.OpenReadAsync();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 1000 };
            await bitmap.SetSourceAsync(stream);
            await new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = file.FileName,
                Content = new Image { Source = bitmap, MaxHeight = 420, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform }, CloseButtonText = "Close" }.ShowAsync();
        }
        catch (Exception ex) { await Ui.Error(this, ex); }
        finally { if (previewPath != null && System.IO.File.Exists(previewPath)) System.IO.File.Delete(previewPath); }
    }

    private async void AttachmentPreview_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image preview) await LoadAttachmentPreviewAsync(preview);
    }
    private async void AttachmentPreview_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is Image preview) await LoadAttachmentPreviewAsync(preview);
    }
    private async Task LoadAttachmentPreviewAsync(Image preview)
    {
        if (preview.DataContext is not MessageItemViewModel { HasImageAttachment: true, FileId: not null } message) return;
        if (preview.Tag as string == message.FileId) return;
        preview.Tag = message.FileId;
        preview.Source = null;
        string? path = null;
        try
        {
            var api = App.Services.GetRequiredService<Services.ClientRequests>();
            var file = await api.GetAsync<Services.SharedFile>($"api/v1/files/{message.FileId}/info");
            if (file.SizeBytes > 10 * 1024 * 1024) { preview.Visibility = Visibility.Collapsed; return; }
            path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + Path.GetExtension(file.FileName));
            await api.DownloadAsync(file, path);
            var storage = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            using var stream = await storage.OpenReadAsync();
            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage { DecodePixelWidth = 640 };
            await bitmap.SetSourceAsync(stream);
            if (preview.DataContext == message) preview.Source = bitmap;
        }
        catch { if (preview.DataContext == message) { preview.Tag = null; preview.Visibility = Visibility.Collapsed; } }
        finally { if (path != null && System.IO.File.Exists(path)) System.IO.File.Delete(path); }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MessageItemViewModel { FileId: not null } message }) return;
        try { var api = App.Services.GetRequiredService<Services.ClientRequests>(); var file = await api.GetAsync<Services.SharedFile>($"api/v1/files/{message.FileId}/info");
            var path = await Ui.SavePath(file.FileName, Path.GetExtension(file.FileName)); if (path != null) await api.DownloadAsync(file, path); } catch (Exception ex) { await Ui.Error(this, ex); }
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_conversationId == null) return;
        var query = new TextBox { PlaceholderText = "Search message history" };
        var results = new ListView { ItemTemplate = Ui.Template(Ui.Text("SenderDisplayName", 12) + Ui.Text("Content") + Ui.Text("SentAt", 12)), MaxHeight = 320 };
        var panel = new StackPanel { Spacing = 12 }; panel.Children.Add(query); panel.Children.Add(Ui.Button("Search", async () =>
        { var page = await App.Services.GetRequiredService<Services.ClientRequests>().GetAsync<Services.MessagePage>($"api/v1/conversations/{_conversationId}/messages?q={Uri.EscapeDataString(query.Text)}&limit=100"); results.ItemsSource = page.Messages; })); panel.Children.Add(results);
        await new ContentDialog { RequestedTheme = ActualTheme, XamlRoot = XamlRoot, Title = "Search conversation", Content = panel, CloseButtonText = "Close" }.ShowAsync();
    }

    private void Emoji_Click(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        foreach (var emoji in new[] { "👍", "❤️", "😊", "👏", "✅", "⚠️" })
        { var item = new MenuFlyoutItem { Text = emoji }; item.Click += (_, _) => MessageInput.Text += emoji; menu.Items.Add(item); }
        menu.ShowAt((FrameworkElement)sender);
    }

    private void LoadMore_Click(object sender, RoutedEventArgs e) =>
        _ = _vm.LoadMoreHistoryCommand.ExecuteAsync(null);

    // ── Animations ────────────────────────────────────────────────────────────

    private async Task AnimateSendButton()
    {
        if (!Services.ThemeTokens.AnimationsEnabled) return;
        var sb = new Storyboard();
        var scale = new DoubleAnimationUsingKeyFrames();
        Storyboard.SetTarget(scale, SendButton);
        Storyboard.SetTargetProperty(scale, "(UIElement.RenderTransform).(ScaleTransform.ScaleX)");
        scale.KeyFrames.Add(new EasingDoubleKeyFrame
            { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame
            { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)), Value = 0.85 });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame
            { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160)), Value = 1.1 });
        scale.KeyFrames.Add(new EasingDoubleKeyFrame
            { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), Value = 1 });

        SendButton.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        SendButton.RenderTransform = new Microsoft.UI.Xaml.Media.ScaleTransform();
        sb.Children.Add(scale);
        sb.Begin();
        await Task.Delay(220);
    }

    private void StartTypingDotAnimation()
    {
        _typingDotStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };

        void AddDotBounce(Ellipse dot, TimeSpan delay)
        {
            var anim = new DoubleAnimationUsingKeyFrames();
            Storyboard.SetTarget(anim, dot);
            Storyboard.SetTargetProperty(anim, "(UIElement.RenderTransform).(TranslateTransform.Y)");
            anim.KeyFrames.Add(new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(delay), Value = 0,
                  EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            anim.KeyFrames.Add(new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(200)), Value = -5,
                  EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            anim.KeyFrames.Add(new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(delay + TimeSpan.FromMilliseconds(400)), Value = 0,
                  EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
            anim.KeyFrames.Add(new EasingDoubleKeyFrame
                { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1200)), Value = 0 });

            dot.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform();
            _typingDotStoryboard.Children.Add(anim);
        }

        AddDotBounce(Dot1, TimeSpan.Zero);
        AddDotBounce(Dot2, TimeSpan.FromMilliseconds(150));
        AddDotBounce(Dot3, TimeSpan.FromMilliseconds(300));
    }
}

