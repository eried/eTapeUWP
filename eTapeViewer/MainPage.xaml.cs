using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Media.Core;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using eTapeViewer.Annotations;

namespace eTapeViewer
{
    public sealed partial class MainPage
    {
        private GattCharacteristic _currentDevice;
        private MediaElement _beep;
        private ObservableCollection<MeasuredValue> _receivedValues;

        public MainPage()
        {
            InitializeComponent();

            _receivedValues = new ObservableCollection<MeasuredValue>();
            listViewValues.ItemsSource = _receivedValues;

            // Do not sleep
            new Windows.System.Display.DisplayRequest().RequestActive();
        }

        private void buttonScan_Click(object sender, RoutedEventArgs e)
        {
            ScanForDevices();
        }

        private async void ScanForDevices()
        {
            foreach(var d in await GetBluetoothDevices())
                if (d.Name == "eTape")
                {
                    ConnectToTape(d);
                    break;
                }
        }

        private async void ConnectToTape(DeviceInformation device)
        {
            if (device == null) return; // Nothing useful here

            var myTape = await GattDeviceService.FromIdAsync(device.Id);

            foreach (var s in myTape.Device.GattServices)
            {
                // TODO: Clean the service Uuid thing
                if (!s.Uuid.ToString().StartsWith("23455100-")) continue; // Not the right service

                foreach (var s2 in s.GetAllCharacteristics())
                    if (s2.Uuid.ToString().StartsWith("23455102-"))
                        if (s2.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                        {
                            if (_currentDevice != s2)
                            {
                                _currentDevice = s2;
                                s2.ValueChanged += S_ValueChanged;
                                break;
                            }
                        }
            }
        }

        private static async Task<DeviceInformationCollection> GetBluetoothDevices()
        {
            return await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.DeviceInformation), null);
        }

        private void S_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var b = DataReader.FromBuffer(args.CharacteristicValue);

            var bytes = new byte[b.UnconsumedBufferLength];
            b.ReadBytes(bytes);

            _receivedValues.Add(new MeasuredValue(ConvertToMillimeters(bytes)));
            sendButton.IsEnabled = true;
        }

        private static double ConvertToMillimeters(byte[] bytes)
        {
            // The report sends units in 1/64 of an inch - sigh...
            return bytes.Length == 2 ? BitConverter.ToInt16(bytes, 0)*0.396875 : -1;
        }

        private void buttonClear_Click(object sender, RoutedEventArgs e)
        {
            _receivedValues.Clear();
            _receivedValues.Add(new MeasuredValue(10));
            _receivedValues.Add(new MeasuredValue(20) { Comments = "hello"});
            sendButton.IsEnabled = _receivedValues.Count > 0;
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

        private void listViewItemValues_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            FlyoutBase.ShowAttachedFlyout(sender as FrameworkElement);
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
                SelectedText = v.Comments ?? "",
            });

            dialog.Content = panel;

            dialog.PrimaryButtonText = "Save";
            dialog.SecondaryButtonText = "Cancel";

            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                v.Comments = ((TextBox)((StackPanel)dialog.Content).Children[0]).Text;
        }

        private void CopyItem_Click(object sender, RoutedEventArgs e)
        {
            CopyToClipboard(GetMeasuredValue(e),false);
        }

        private static MeasuredValue GetMeasuredValue(RoutedEventArgs e)
        {
            return (MeasuredValue)((MenuFlyoutItem)e.OriginalSource).DataContext;
        }

        private static void CopyToClipboard(MeasuredValue m, bool copyValue)
        {
            var d = new DataPackage();
            d.SetText(copyValue?m.Millimeters+"":m.Output);
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
        }

        private void toggleButtonBeep_Unchecked(object sender, RoutedEventArgs e)
        {
            beepSwitch.Icon = new SymbolIcon(Symbol.Mute);
        }

        private void buttonShare_Click(object sender, RoutedEventArgs e)
        {
            throw new NotImplementedException();
        }
    }

    public class MeasuredValue : INotifyPropertyChanged
    {
        private string _comments;
        public double Millimeters { get; set; }

        public MeasuredValue(double millimeters)
        {
            Millimeters = millimeters;
        }

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