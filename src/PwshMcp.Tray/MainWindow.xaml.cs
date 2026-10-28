using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PwshMcp.Tray;

public partial class MainWindow : Window
{
    readonly MainVm vm;

    public MainWindow(MainVm vm)
    {
        this.vm = vm;
        Language = L.Xml;
        InitializeComponent();
        DataContext = vm;
        vm.PropertyChanged += OnVm;
        IsVisibleChanged += (_, _) => vm.Viewing = IsVisible && WindowState != WindowState.Minimized;
        StateChanged += (_, _) => vm.Viewing = IsVisible && WindowState != WindowState.Minimized;
        InputBindings.Add(new KeyBinding(new Relay(() => vm.Tab = Tab.Requests), Key.D1, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => vm.Tab = Tab.Access), Key.D2, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(() => vm.Tab = Tab.Tasks), Key.D3, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new Relay(Hide), Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(vm.Retry, Key.F5, ModifierKeys.None));
    }

    object? shown;

    void OnVm(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(MainVm.Selected) or "" or null)) return;
        if (ReferenceEquals(shown, vm.Selected)) return;
        shown = vm.Selected;
        if (!Theme.Motion || shown is null) return;
        var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 5 };
        var d = TimeSpan.FromMilliseconds(180);
        detail.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, d) { EasingFunction = ease });
        rise.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new DoubleAnimation(4, 0, d) { EasingFunction = ease });
    }

    /// <summary>Brings the window forward. Only called for explicit user intent (menu, double-click, --show, second launch).</summary>
    public void Present(Tab? tab = null)
    {
        if (tab is { } t) vm.Tab = t;
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        FocusList();
    }

    void FocusList()
    {
        var list = vm.Tab switch { Tab.Requests => reqList, Tab.Access => accList, _ => taskList };
        Dispatcher.BeginInvoke(() =>
        {
            if (list.SelectedItem is { } item && list.ItemContainerGenerator.ContainerFromItem(item) is UIElement c) c.Focus();
            else list.Focus();
        });
    }

    /// <summary>Footer lifetime echo: scrolls the approval card into view and puts focus on the chosen lifetime.</summary>
    void OnPicked(object sender, RoutedEventArgs e)
    {
        var issue = (Style)FindResource(K.Issue);
        var card = Find<Border>(detail, b => b.Style == issue && b.IsVisible);
        if (card is null) return;
        card.BringIntoView();
        Find<RadioButton>(card, r => r.IsChecked == true)?.Focus();
    }

    static T? Find<T>(DependencyObject d, Func<T, bool> ok) where T : DependencyObject
    {
        if (d is T t && ok(t)) return t;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            if (Find(VisualTreeHelper.GetChild(d, i), ok) is { } hit) return hit;
        return null;
    }

    bool retired;

    /// <summary>Adopts the placement of the window this one replaces after a language switch.</summary>
    public void Take(MainWindow old)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;
        var r = old.WindowState == WindowState.Normal ? new Rect(old.Left, old.Top, old.Width, old.Height) : old.RestoreBounds;
        if (!r.IsEmpty) { Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height; }
        if (old.WindowState == WindowState.Maximized) WindowState = WindowState.Maximized;
    }

    /// <summary>Closes for real (replaced, not hidden). The view model lives on in the new window.</summary>
    public void Retire()
    {
        retired = true;
        vm.PropertyChanged -= OnVm;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing the window keeps the tray running; Exit lives in the tray menu.
        if (!App.Quitting && !retired) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
