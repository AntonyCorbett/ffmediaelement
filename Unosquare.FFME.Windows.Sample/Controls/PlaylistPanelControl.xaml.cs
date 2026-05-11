namespace Unosquare.FFME.Windows.Sample.Controls
{
    using Foundation;
    using System;
    using System.Windows;
    using System.Windows.Controls.Primitives;
    using System.Windows.Input;
    using ViewModels;

    /// <summary>
    /// Interaction logic for PlaylistPanelControl.xaml.
    /// </summary>
    public partial class PlaylistPanelControl
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PlaylistPanelControl"/> class.
        /// </summary>
        public PlaylistPanelControl()
        {
            InitializeComponent();

            // Prevent binding to the events
            if (App.IsInDesignMode)
                return;

            // Bind the Enter key to the command
            OpenFileTextBox.KeyDown += async (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                await App.ViewModel.Commands.OpenCommand.ExecuteAsync(OpenFileTextBox.Text);
                e.Handled = true;
            };

            SearchTextBox.IsEnabledChanged += (s, e) =>
            {
                if ((bool)e.OldValue == false && (bool)e.NewValue)
                    FocusSearchBox();

                if ((bool)e.OldValue && (bool)e.NewValue == false)
                    FocusFileBox();
            };

            IsVisibleChanged += (s, e) =>
            {
                if (SearchTextBox.IsEnabled)
                    FocusSearchBox();
                else
                    FocusFileBox();
            };
        }

        /// <summary>
        /// A proxy, strongly-typed property to the underlying DataContext.
        /// </summary>
        public RootViewModel ViewModel => DataContext as RootViewModel;

#pragma warning disable U2U1012
        private static void FocusTextBox(TextBoxBase textBox)
#pragma warning restore U2U1012
        {
            DeferredAction.Create(context =>
            {
                if (textBox == null || Application.Current == null || Application.Current.MainWindow == null)
                    return;

                textBox.Focus();
                textBox.SelectAll();
                FocusManager.SetFocusedElement(Application.Current.MainWindow, textBox);
                Keyboard.Focus(textBox);

                if (textBox.IsVisible == false || textBox.IsKeyboardFocused)
                    context?.Dispose();
                else
                    context?.Defer(TimeSpan.FromSeconds(0.25));
            }).Defer(TimeSpan.FromSeconds(0.25));
        }

        /// <summary>
        /// Focuses the search box.
        /// </summary>
        private void FocusSearchBox()
        {
            FocusTextBox(SearchTextBox);
        }

        /// <summary>
        /// Focuses the file box.
        /// </summary>
        private void FocusFileBox()
        {
            FocusTextBox(OpenFileTextBox);
        }
    }
}
