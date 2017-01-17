using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace eTapeViewer
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class Tutorial : Page
    {
        public Tutorial()
        {
            this.InitializeComponent();
            SystemNavigationManager.GetForCurrentView().BackRequested += OnBackRequested;

            this.Unloaded += Tutorial_Unloaded;
        }

        private void Tutorial_Unloaded(object sender, RoutedEventArgs e)
        {
            SystemNavigationManager.GetForCurrentView().BackRequested -= OnBackRequested;
        }

        private void OnBackRequested(object sender, BackRequestedEventArgs e)
        {
            if (tutorialFlipView.SelectedIndex > 0)
            {
                tutorialFlipView.SelectedIndex--;
                e.Handled = true;
            }
            else if (Frame.CanGoBack)
            {
                Frame.GoBack();
                e.Handled = true;
            }
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
                Frame.GoBack();
            else
            {
                Frame.Navigate(typeof(MainPage));
                Frame.BackStack.Clear();
            }
        }

        private void buttonNext_Click(object sender, RoutedEventArgs e)
        {
            tutorialFlipView.SelectedIndex += 1;
            nextStep.Visibility = Visibility.Collapsed;
        }

        private void tutorialFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if(tutorialFlipView.SelectedIndex > 0)
                nextStep.Visibility = Visibility.Collapsed;
        }
    }
}
