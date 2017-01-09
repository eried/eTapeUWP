using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Display;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using eTapeViewer.Annotations;

namespace eTapeViewer
{
    public sealed partial class MainPage
    {
        private MediaElement _beep;
        private MeasuredValue _clickedItem;
        private GattCharacteristic _currentDevice;
        private readonly ObservableCollection<MeasuredValue> _receivedValues;
        private string _currentConfiguration;
        private bool _ignoreFlyout;

        public MainPage()
        {
            InitializeComponent();

            _receivedValues = new ObservableCollection<MeasuredValue>();
            listViewValues.ItemsSource = _receivedValues;

            DataTransferManager.GetForCurrentView().DataRequested += MainPage_DataRequested;

            flyout.Closing += Flyout_Closing;
            flyout.Opening += Flyout_Opening;

            if (Debugger.IsAttached)
            {
                // Test values
                _receivedValues.Add(new MeasuredValue(10.2));
                _receivedValues.Add(new MeasuredValue(11.7));
                _receivedValues.Add(new MeasuredValue(20.1) {Comments = "hello"});
            }

            // Do not sleep
            new DisplayRequest().RequestActive();
        }

        private void Flyout_Opening(object sender, object e)
        {
            _ignoreFlyout = true;
            Debug.WriteLine("OPENING");
        }

        private void Flyout_Closing(Windows.UI.Xaml.Controls.Primitives.FlyoutBase sender, Windows.UI.Xaml.Controls.Primitives.FlyoutBaseClosingEventArgs args)
        {
            _ignoreFlyout = false;
            Debug.WriteLine("CLOSING");
        }

        private void MainPage_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
        {
            if (_receivedValues.Count > 0)
            {
                //var s = new StringBuilder("<style type=\"text/css\"> table.tableizer-table { font-size: 12px; border: 1px solid #CCC; font-family: Arial, Helvetica, sans-serif; } .tableizer-table td { padding: 4px; margin: 3px; border: 1px solid #CCC; } .tableizer-table th { background-color: #104E8B; color: #FFF; font-weight: bold; } </style> <table class=\"tableizer-table\"><thead><tr class=\"tableizer-firstrow\"><th>Value</th><th>Units</th><th>Comments</th></tr></thead><tbody>");
                var s = new StringBuilder("Value,Unit,Comments" + Environment.NewLine);

                foreach (var v in _receivedValues)
                    s.AppendLine($"{v.Millimeters},mm,{v.Comments}");
                //s.Append("</tbody></table>");
                //var htmlFormat = HtmlFormatHelper.CreateHtmlFormat(s.ToString());
                //args.Request.Data.SetHtmlFormat(htmlFormat);
                //args.Request.Data.SetRtf(rtf)
                args.Request.Data.SetText(s.ToString());
                args.Request.Data.Properties.Title = "Capture - " + Package.Current.DisplayName;
            }
        }

        private void buttonScan_Click(object sender, RoutedEventArgs e)
        {
            ScanForDevices();
        }

        private async void ScanForDevices()
        {
            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    buttonConnect.Icon = new SymbolIcon(Symbol.ZeroBars);
                });

            foreach (var d in await GetBluetoothDevices())
                if (d.Name == "eTape")
                {
                    ConnectToTape(d);
                    //break;
                }
        }

        private async void ConnectToTape(DeviceInformation device)
        {
            if (device == null) return; // Nothing useful here

            var myTape = await GattDeviceService.FromIdAsync(device.Id);
            myTape.Device.ConnectionStatusChanged += Device_ConnectionStatusChanged;

            if (myTape.Device.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected)
            {
                foreach (var s in myTape.Device.GattServices)
                {
                    // TODO: Clean the service Uuid thing
                    if (!s.Uuid.ToString().StartsWith("23455100-")) continue; // Not the right service

                    foreach (var s2 in s.GetAllCharacteristics())
                    {
                        var c = s2.Uuid.ToString();
                        if (c.StartsWith("23455107-"))
                        {
                            // Tape configuration
                            var r = await s2.ReadValueAsync(Windows.Devices.Bluetooth.BluetoothCacheMode.Uncached);

                            var b = DataReader.FromBuffer(r.Value);

                            var bytes = new byte[b.UnconsumedBufferLength];
                            b.ReadBytes(bytes);

                            var bits = new BitArray(bytes);
                            _currentConfiguration = "Device configuration\n" +
                                                    $"Battery low: {bits[15]}\n" +
                                                    $"Inside measurement: {bits[14]}\n" +
                                                    $"Offset measurement: {bits[13]}\n" +
                                                    $"Centerline: {bits[12]}\n" +
                                                    $"Display units: {(bits[2] ? "Metric" : (bits[0] && bits[1] ? "Decimal feet" : (bits[1] ? "Decimal inches" : (bits[0] ? "Fractional inches" : "Feet and inches"))))}\n\n"+
                                                    "This information will be updated when connecting to the tape. The application uses Metric and ignores all the other settings.";
                        }

                        if (c.StartsWith("23455102-"))
                            if (s2.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                            {
                                await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                                        CoreDispatcherPriority.Normal,
                                        () =>
                                        {
                                            buttonConnect.Icon = new SymbolIcon(Symbol.FourBars);
                                        });

                                if ((_currentDevice == null) || (_currentDevice.Uuid != s2.Uuid))
                                {
                                    _currentDevice = s2;
                                    _currentDevice.ValueChanged += S_ValueChanged;
                                }
                                //break;
                            }
                    }
                }
            }
        }

        private void Device_ConnectionStatusChanged(Windows.Devices.Bluetooth.BluetoothLEDevice sender, object args)
        {
            ScanForDevices();
        }

        private static async Task<DeviceInformationCollection> GetBluetoothDevices()
        {
            return
                await
                    DeviceInformation.FindAllAsync(
                        GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.DeviceInformation), null);
        }

        private async void S_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var b = DataReader.FromBuffer(args.CharacteristicValue);

            var bytes = new byte[b.UnconsumedBufferLength];
            b.ReadBytes(bytes);

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () =>
                {
                    var m = new MeasuredValue(ConvertToMillimeters(bytes));
                    _receivedValues.Add(m);
                    listViewValues.ScrollIntoView(m);

                    sendButton.IsEnabled = true;
                    clearButton.IsEnabled = true;

                    if ((bool) beepSwitch.IsChecked)
                        _beep?.Play();
                }
            );
        }

        private static double ConvertToMillimeters(byte[] bytes)
        {
            // The report sends units in 1/64 of an inch - sigh...
            return bytes.Length == 2 ? BitConverter.ToInt16(bytes, 0)*0.396875 : -1;
        }

        private async void buttonClear_Click(object sender, RoutedEventArgs e)
        {
            var d = new MessageDialog("Delete all measurements?");
            var yes = new UICommand("Yes");
            d.Commands.Add(yes);
            d.Commands.Add(new UICommand("No"));

            if (await d.ShowAsync() == yes)
            {
                _receivedValues.Clear();

                clearButton.IsEnabled = false;
                sendButton.IsEnabled = false;
            }
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            _beep = new MediaElement {AutoPlay = false};
            _beep.SetPlaybackSource(MediaSource.CreateFromUri(new Uri("ms-appx:///Assets/beep.mp3")));

            ScanForDevices();
        }

        private void toggleButtonBeep_Checked(object sender, RoutedEventArgs e)
        {
            _beep?.Play();
            beepSwitch.Icon = new SymbolIcon(Symbol.Volume);
        }

        private async void AddCommentsItem_Click(object sender, RoutedEventArgs e)
        {
            var v = GetMeasuredValue(e);
            var dialog = new ContentDialog {Title = "Comments"};
            var panel = new StackPanel();

            panel.Children.Add(new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                PlaceholderText = "Details about this entry",
                //Text = v.Comments ?? "",
                SelectedText = v.Comments ?? ""
            });

            dialog.Content = panel;
            
            dialog.PrimaryButtonText = "Save";
            dialog.SecondaryButtonText = "Cancel";

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                v.Comments = ((TextBox) ((StackPanel) dialog.Content).Children[0]).Text;
        }

        private void CopyItem_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(GetMeasuredValue(e), false);
        }

        private MeasuredValue GetMeasuredValue(RoutedEventArgs e)
        {
            return (MeasuredValue) (e is ItemClickEventArgs
                ?  ((ItemClickEventArgs) e).ClickedItem
                : ((FrameworkElement) e.OriginalSource).DataContext);
        }

        private static void CopyToClipboard(MeasuredValue m, bool copyValue)
        {
            var d = new DataPackage();
            d.SetText(copyValue ? m.Millimeters + "" : m.Output);
            Clipboard.SetContent(d);
        }

        private void CopyValueItem_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(GetMeasuredValue(e), true);
        }

        private void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            _receivedValues.Remove(GetMeasuredValue(e));
            sendButton.IsEnabled = _receivedValues.Count > 0;
            clearButton.IsEnabled = sendButton.IsEnabled;
        }

        private void toggleButtonBeep_Unchecked(object sender, RoutedEventArgs e)
        {
            beepSwitch.Icon = new SymbolIcon(Symbol.Mute);
        }

        private void buttonShare_Click(object sender, RoutedEventArgs e)
        {
            DataTransferManager.ShowShareUI();
        }

        private void listViewValues_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            Debug.WriteLine("RIGHT TAP");
            if (!_ignoreFlyout)
            {
                ShowFlyout(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
                Debug.WriteLine("RIGHT TAP FLYOUT");
            }
        }

        private void ShowFlyout(FrameworkElement f, Point p)
        {
            if (f?.DataContext != null)
                flyout.ShowAt(f, p);
        }

        private async void DeviceInfoButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new MessageDialog(string.IsNullOrEmpty(_currentConfiguration)?"No information available.":_currentConfiguration);
            await dialog.ShowAsync();
        }

        private void listViewValues_ItemClick(object sender, ItemClickEventArgs e)
        {
            AddCommentsItem_Click(sender, e);
        }

        private void listViewValues_Holding(object sender, HoldingRoutedEventArgs e)
        {
            Debug.WriteLine("HOLDING");
            if (!_ignoreFlyout)
            {
                ShowFlyout(e.OriginalSource as FrameworkElement, e.GetPosition(e.OriginalSource as UIElement));
                Debug.WriteLine("HOLDING FLYOUT");
            }
        }
    }

    public class MeasuredValue : INotifyPropertyChanged
    {
        private string _comments;

        public MeasuredValue(double millimeters)
        {
            Millimeters = millimeters;
        }

        public double Millimeters { get; set; }

        public string Output
        {
            get
            {
                return $"{Millimeters} mm ({Math.Round(Millimeters/10.0, 1)} cms)" +
                       (string.IsNullOrEmpty(Comments) ? "" : " " + Comments);
            }
        }

        public string Comments
        {
            get { return _comments; }
            set
            {
                if (value == _comments) return;
                _comments = value;
                OnPropertyChanged(nameof(Output));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        [NotifyPropertyChangedInvocator]
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}