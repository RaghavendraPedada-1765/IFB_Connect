using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace IFB.Client.Services;

internal static class FluidMotion
{
    public static void Scale(FrameworkElement element, float target)
    {
        if (element.XamlRoot == null) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)element.ActualWidth / 2, (float)element.ActualHeight / 2, 0);
        if (!ThemeTokens.AnimationsEnabled)
        {
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
            return;
        }
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(target, target, 1);
        spring.DampingRatio = 1;
        spring.Period = TimeSpan.FromMilliseconds(110);
        // No initial value: begin at the compositor's current presentation value.
        visual.StartAnimation("Scale", spring);
    }

    public static void Attach(Button button)
    {
        button.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Scale(button, .97f)), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => Scale(button, 1)), true);
        button.PointerCanceled += (_, _) => Scale(button, 1);
        button.PointerCaptureLost += (_, _) => Scale(button, 1);
        button.PointerExited += (_, _) => Scale(button, 1);
    }
}
