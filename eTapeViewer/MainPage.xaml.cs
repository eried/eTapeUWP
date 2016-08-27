using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.ApplicationModel.Core;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace eTapeViewer
{
    public sealed partial class MainPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void buttonScan_Click(object sender, RoutedEventArgs e)
        {
            listViewDevices.ItemsSource = await GetBluetoothDevices();
        }

        private static async Task<DeviceInformationCollection> GetBluetoothDevices()
        {
            return await DeviceInformation.FindAllAsync(GattDeviceService.GetDeviceSelectorFromUuid(GattServiceUuids.DeviceInformation), null);
        }

        private async void listViewDevices_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var d in e.AddedItems)
            {
                var device = d as DeviceInformation;
                if (device == null) continue; // Nothing useful here

                var myTape = await GattDeviceService.FromIdAsync(device.Id);

                foreach (var s in myTape.Device.GattServices)
                {
                    // TODO: Clean the service Uuid thing
                    if (!s.Uuid.ToString().StartsWith("23455100-")) continue; // Not the right service

                    foreach (var s2 in s.GetAllCharacteristics())
                        if (s2.Uuid.ToString().StartsWith("23455102-")) 
                            if (s2.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                                s2.ValueChanged += S_ValueChanged;
                }
            }
        }

        private async void S_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            var b = DataReader.FromBuffer(args.CharacteristicValue);

            var bytes = new byte[b.UnconsumedBufferLength];
            b.ReadBytes(bytes);

            await CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(
                CoreDispatcherPriority.Normal, () => 
                {
                    listViewValues.Items.Add($"[{BitConverter.ToString(bytes)}]: {ConvertToMillimeters(bytes)} mm");
                    listViewValues.ScrollIntoView(listViewValues.Items.Last());
                }
            );
        }

        private static double ConvertToMillimeters(byte[] bytes)
        {
            // The report sends units in 1/64 of an inch - sigh...
            return bytes.Length == 2 ? BitConverter.ToInt16(bytes, 0)*0.396875 : -1;
        }

        private void buttonClear_Click(object sender, RoutedEventArgs e)
        {
            listViewValues.Items.Clear();
        }
    }
}