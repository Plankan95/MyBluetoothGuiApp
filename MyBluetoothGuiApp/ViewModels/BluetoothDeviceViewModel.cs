using System;
using System.ComponentModel; // Behövs för INotifyPropertyChanged
//using System.Runtime.CompilerServices; // *** TA BORT DENNA ***
using System.Threading.Tasks; // Behövs för Task
using System.Windows; // Behövs för Application (för resurser som färger)
using System.Windows.Media; // Behövs för SolidColorBrush
using System.Windows.Threading; // Behövs för Dispatcher i WPF
using Windows.Devices.Bluetooth; // Behövs för BluetoothDevice, BluetoothConnectionStatus
using Windows.Devices.Enumeration; // Behövs för DeviceInformation, DeviceInformationUpdate
using Windows.Foundation; // Behövs för IAsyncOperation
using System.Diagnostics; // Behövs för Debug.WriteLine

namespace BluetoothManager.ViewModels
{
    // Denna klass representerar en enskild Bluetooth-enhet i listan i din app.
    // Den är utformad för att "binda" (koppla) till UI-element i XAML.
    // Den implementerar INotifyPropertyChanged för att UI:t ska uppdateras automatiskt
    // när egenskaper som t.ex. anslutningsstatus ändras.
    public class BluetoothDeviceViewModel : INotifyPropertyChanged, IDisposable
    {
        // Referens till den råa informationen vi fick från DeviceWatcher.
        private readonly DeviceInformation _deviceInfo;

        // Referens till BluetoothDevice-objektet. Detta objekt kan ge oss
        // anslutningsstatus och låter oss prenumerera på statusändringar.
        // Det här objektet kan vara null initialt eftersom det laddas asynkront.
        private BluetoothDevice? _bluetoothDevice; // Markerad som nullable (med ?) för att hantera potentiellt null värde

        // Referens till UI-trådens Dispatcher. Behövs för att säkert uppdatera
        // UI-element från bakgrundstrådar (där ConnectionStatusChanged-eventet körs).
        private Dispatcher _dispatcher;

        // --- Egenskaper som UI:t binder till ---

        // Enhetens namn. När detta ändras, notifieras UI:t.
        private string _name;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value, nameof(Name)); // <-- Använd nameof() explicit
        }

        // Enhetens anslutningsstatus (t.ex. "Ansluten", "Frånkopplad", "Laddar...").
        // När detta ändras, notifieras UI:t.
        private string _connectionStatus = "Initialiserar..."; // Sätt ett initialt värde här för att undvika nullability warning/error
        public string ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                if (SetProperty(ref _connectionStatus, value, nameof(ConnectionStatus))) // <-- Använd nameof() explicit
                {
                    // När status ändras, kan färgen och ikonen för statusen också behöva uppdateras.
                    // Vi notifierar UI:t att dessa beroende egenskaper också kan ha ändrats.
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(StatusIconGlyph));
                }
            }
        }

        // Färgen på status-indikatorn (t.ex. grön för ansluten, röd för frånkopplad).
        // Denna egenskap hämtar färgen från de resurser som definieras i temafilen.
        public SolidColorBrush StatusColor => ConnectionStatus == "Ansluten"
            ? (SolidColorBrush)Application.Current.Resources["ConnectedStatusColor"] // Hämta från temaresurser
            : (SolidColorBrush)Application.Current.Resources["DisconnectedStatusColor"]; // Hämta från temaresurser

        // Ikonen som representerar anslutningsstatus (Segoe MDL2 Assets teckensnitt).
        // \xE73E = Checkmark (Ansluten)
        // \xE711 = X (Frånkopplad/Ej ansluten)
        public string StatusIconGlyph => ConnectionStatus == "Ansluten" ? "\xE73E" : "\xE711";

        // Ikonen som representerar enhetstypen (t.ex. hörlurar, mus). Segoe MDL2 Assets.
        private string _typeIconGlyph = "\xE7BA"; // Sätt en standardikon initialt
        public string TypeIconGlyph
        {
            get => _typeIconGlyph;
            set => SetProperty(ref _typeIconGlyph, value, nameof(TypeIconGlyph)); // <-- Använd nameof() explicit
        }

        // Enhetens unika identifierare (ID). Används för att matcha uppdateringar/borttagningar.
        public string Id => _deviceInfo.Id;

        // Referens till den underliggande DeviceInformation. Används internt.
        public DeviceInformation DeviceInformation => _deviceInfo;

        // Konstruktorn. Skapas av MainWindowViewModel när en enhet hittas.
        // Den får den initiala DeviceInformation från Service och en referens till Dispatchern.
        public BluetoothDeviceViewModel(DeviceInformation deviceInfo, Dispatcher dispatcher)
        {
            _deviceInfo = deviceInfo;
            // Sätt initialt namn baserat på informationen från Windows.
            _name = deviceInfo.Name;
            _dispatcher = dispatcher;

            // Försök bestämma och sätta en lämplig ikon för enhetstypen.
            DetermineAndSetTypeIcon();

            // VIKTIGT: Vi kan inte få anslutningsstatusen direkt från DeviceInformation.
            // Vi måste hämta ett BluetoothDevice-objekt asynkront och prenumerera på dess event.
            // Vi använder "_ =" för att starta denna operation och inte blockera konstruktorn.
            // Eventuella fel måste hanteras inuti LoadBluetoothDeviceAndMonitorStatusAsync.
            _ = LoadBluetoothDeviceAndMonitorStatusAsync(); // "Fire and forget" Task
        }

        // Asynkron metod för att hämta BluetoothDevice-objektet och börja övervaka dess status.
        private async Task LoadBluetoothDeviceAndMonitorStatusAsync()
        {
            // Sätt en tillfällig status medan vi laddar.
            UpdateStatus("Laddar...");

            try
            {
                // Försök hämta BluetoothDevice-objektet med enhetens ID.
                // Detta anrop är asynkront ('await').
                _bluetoothDevice = await BluetoothDevice.FromIdAsync(_deviceInfo.Id);

                // Kolla om vi lyckades hämta objektet.
                if (_bluetoothDevice != null)
                {
                    // Nu har vi BluetoothDevice-objektet, vi kan få den aktuella statusen.
                    UpdateStatusFromBluetoothDevice();

                    // VIKTIGT: Prenumerera på händelsen som triggas när anslutningsstatusen ändras.
                    // Denna event kommer att anropas på en bakgrundstråd.
                    _bluetoothDevice.ConnectionStatusChanged += BluetoothDevice_ConnectionStatusChanged;
                }
                else
                {
                    // Kunde inte hämta BluetoothDevice-objektet. Kanske enheten är offline,
                    // eller Windows kunde inte ge oss ett BluetoothDevice för just den här enheten.
                    Debug.WriteLine($"BluetoothDeviceViewModel: Kunde inte hämta BluetoothDevice för {_deviceInfo.Name} ({_deviceInfo.Id})");
                    UpdateStatus("Ej tillgänglig"); // Sätt status till något som indikerar problem
                }
            }
            catch (Exception ex)
            {
                // Ett oväntat fel inträffade vid hämtning av BluetoothDevice.
                // Detta kan t.ex. hända om Bluetooth är avstängt i Windows.
                Debug.WriteLine($"BluetoothDeviceViewModel: Fel vid hämtning av BluetoothDevice för {_deviceInfo.Name}: {ex.Message}");
                UpdateStatus("Fel"); // Visa ett felmeddelande i statusen
            }
        }

        // Hjälpmetod för att säkert uppdatera ConnectionStatus på UI-tråden.
        // Använd ALLTID denna metod när du ändrar ConnectionStatus från en bakgrundstråd.
        private void UpdateStatus(string status)
        {
            // _dispatcher.Invoke() ser till att den medföljande koden (en lambda/anonym funktion)
            // körs på den tråd som Dispatcher tillhör (UI-tråden i detta fall).
            _dispatcher.Invoke(() =>
            {
                ConnectionStatus = status;
            });
        }

        // Hjälpmetod för att läsa av statusen från _bluetoothDevice och uppdatera ViewModel.
        private void UpdateStatusFromBluetoothDevice()
        {
            // Om _bluetoothDevice är null (vilket den inte borde vara om denna metod kallas),
            // kan vi inte läsa statusen. Lägg till en kontroll för säkerhet.
            if (_bluetoothDevice == null)
            {
                UpdateStatus("Okänd status");
                return;
            }

            // Konvertera enum-värdet till en svensk textsträng.
            string status = _bluetoothDevice.ConnectionStatus == BluetoothConnectionStatus.Connected ? "Ansluten" : "Frånkopplad";
            // Anropa den trådsäkra metoden för att uppdatera ViewModel.
            UpdateStatus(status);
        }


        // --- Event Handler för BluetoothDevice ConnectionStatusChanged ---
        // Denna metod anropas av Windows när enhetens anslutningsstatus ändras.
        // VIKTIGT: Den anropas på en bakgrundstråd!
        private void BluetoothDevice_ConnectionStatusChanged(BluetoothDevice sender, object args)
        {
            Debug.WriteLine($"BluetoothDeviceViewModel: Status ändrad för {sender.Name}: {sender.ConnectionStatus}");
            // Anropa metoden som uppdaterar ViewModel på UI-tråden.
            UpdateStatusFromBluetoothDevice();
        }

        // --- Metod för att hantera uppdateringar från DeviceWatcher ---
        // Denna metod anropas av MainWindowViewModel när DeviceService skickar en Update-event.
        internal void UpdateDeviceInfo(DeviceInformationUpdate update)
        {
            // Uppdatera den underliggande DeviceInformation-objektet med den nya infon.
            _deviceInfo.Update(update);

            // Kolla om enhetens namn finns med i uppdateringen och uppdatera vår Name-egenskap.
            // Måste köras på UI-tråden.
            if (_deviceInfo.Properties.ContainsKey("System.ItemNameDisplay"))
            {
                _dispatcher.Invoke(() =>
                {
                    Name = (string)_deviceInfo.Properties["System.ItemNameDisplay"];
                });
            }

            // Notera: Denna metod uppdaterar inte ConnectionStatus. Det gör ConnectionStatusChanged-eventet.
        }


        // --- Logik för att bestämma enhetstyp-ikon ---
        // Denna metod försöker tolka Class of Device (CoD) informationen
        // för att välja en lämplig ikon från Segoe MDL2 Assets.
        private void DetermineAndSetTypeIcon()
        {
            // CoD är en siffra som beskriver enhetstyp. Den finns i DeviceInformation.Properties.
            object? classOfDeviceObj; // Kan vara null, markera som nullable
            if (_deviceInfo.Properties.TryGetValue("System.Devices.Aep.Bluetooth.ClassOfDevice", out classOfDeviceObj) && classOfDeviceObj is uint classOfDevice) // Kontrollera både existens och typ
            {
                TypeIconGlyph = GetIconFromClassOfDevice(classOfDevice);
            }
            else
            {
                // Om CoD-informationen saknas, använd en standard Bluetooth-ikon.
                TypeIconGlyph = "\xE7BA"; // Standard Bluetooth-ikon
            }
        }

        // Enkel mappningsfunktion från Class of Device (CoD) till Segoe MDL2 Assets ikon.
        // Detta är en FÖRENKLAD mappning. En korrekt implementering är mer komplex.
        // CoD-värden är bitfält. Se Bluetooth Assigned Numbers för detaljer.
        // (https://bluetooth-msft.github.io/bluetooth/pages/assigned-numbers.html)
        private string GetIconFromClassOfDevice(uint cod)
        {
            // Mask för huvudklass (Major Device Class - bit 13-8)
            uint majorDeviceClass = (cod & 0x00001F00) >> 8;

            // Vissa vanliga huvudklasser
            const uint MajorDevice_Computer = 0x01;
            const uint MajorDevice_Phone = 0x02;
            const uint MajorDevice_AudioVideo = 0x04;
            const uint MajorDevice_Peripheral = 0x05; // Möss, tangentbord, joysticks etc.
            const uint MajorDevice_Wearable = 0x07; // Smartwatches, fitnesstrackers

            // Kolla huvudklasser
            if (majorDeviceClass == MajorDevice_AudioVideo)
            {
                // För Audio/Video, kolla Minor Device Class (bit 7-2) för mer specifik typ.
                uint minorDeviceClass = (cod & 0xFC) >> 2;

                if (minorDeviceClass >= 0x01 && minorDeviceClass <= 0x08) return "\xE90F"; // Headset/Hörlurar (ca minor 1-8)
                if (minorDeviceClass >= 0x09 && minorDeviceClass <= 0x0C) return "\xEF7A"; // Högtalare (ca minor 9-12)
                // ... lägg till fler A/V typer om du vet deras CoD-värden
                return "\xE909"; // Standard Audio-ikon som fallback
            }
            else if (majorDeviceClass == MajorDevice_Peripheral)
            {
                // För Peripheral, kolla Minor Device Class och/eller andra flaggor.
                // HID-flaggan (bit 7, 0x80) och Peripheral-flaggan (bit 6, 0x40) är ofta satta tillsammans.
                if ((cod & 0xC0) == 0xC0) // Är det en HID Peripheral?
                {
                    uint minorDeviceClass = (cod & 0xFC) >> 2;
                    // Exempel på mindre klasser för HID (kräver noggrann CoD-mappning!)
                    // Dessa är grova gissningar/exempel:
                    if (minorDeviceClass >= (0x80 >> 2) && minorDeviceClass <= (0xAC >> 2)) return "\xED52"; // Mus (ca minor 0x20-0x2B) -- Använd skiftade värden här
                    if (minorDeviceClass >= (0x40 >> 2) && minorDeviceClass <= (0x6C >> 2)) return "\xE911"; // Tangentbord (ca minor 0x10-0x1B) -- Använd skiftade värden här
                                                                                                             // ... fler HID-typer som joystick, gamepad, digitizer etc.
                    return "\xE956"; // Standard Peripheral-ikon om vi inte kan avgöra mus/tangentbord
                }
                return "\xE956"; // Standard Peripheral-ikon om inte HID
            }
            else if (majorDeviceClass == MajorDevice_Phone)
            {
                return "\xE8EA"; // Mobiltelefon-ikon
            }
            else if (majorDeviceClass == MajorDevice_Computer)
            {
                return "\xE958"; // Dator-ikon
            }
            else if (majorDeviceClass == MajorDevice_Wearable)
            {
                // Kolla Minor Device Class för Wearable
                uint minorDeviceClass = (cod & 0xFC) >> 2;
                if (minorDeviceClass == 0x01) return "\xE955"; // Klocka (Watch)
                if (minorDeviceClass == 0x02) return "\xEC4E"; // Fitness Tracker
                                                               // ... andra wearable-typer
                return "\xE7BA"; // Standard Bluetooth som fallback för Wearable
            }


            // ... lägg till fler huvudklasser vid behov (Networking, Imaging, etc.)

            return "\xE7BA"; // Standard Bluetooth-ikon som sista fallback
        }


        // --- Implementerar IDisposable ---
        // Denna metod kallas när ViewModel-objektet inte längre används
        // (t.ex. när enheten tas bort från listan eller appen stängs).
        // Det är VÄLDIGT viktigt att avprenumerera från events här
        // för att undvika minnesläckor och krascher.
        public void Dispose()
        {
            Debug.WriteLine($"BluetoothDeviceViewModel: Disposing for {_name}");

            // Om vi lyckades hämta BluetoothDevice-objektet, avprenumerera från ConnectionStatusChanged
            // och släpp objektet.
            if (_bluetoothDevice != null)
            {
                _bluetoothDevice.ConnectionStatusChanged -= BluetoothDevice_ConnectionStatusChanged;
                // BluetoothDevice implementerar IDisposable i nyare SDKs
                (_bluetoothDevice as IDisposable)?.Dispose(); // Säker konvertering och anrop
                _bluetoothDevice = null; // Nolla referensen
            }
            // Vi behöver inte göra något särskilt med _deviceInfo här.
        }


        // --- Standard INotifyPropertyChanged Implementation ---
        // Denna kod är standard och krävs för att UI-bindningar ska fungera korrekt.
        // Du kan kopiera och klistra in den i alla ViewModels som behöver rapportera
        // ändringar till UI:t.

        public event PropertyChangedEventHandler? PropertyChanged; // Markerad som nullable event

        // Hjälpmetod för att trigga PropertyChanged-eventet.
        // propertyName skickas nu in explicit med nameof().
        protected virtual void OnPropertyChanged(string? propertyName) // <-- Ändrad signatur, ingen [CallerMemberNames], ingen default null
        {
            // Kolla om PropertyChanged har några prenumeranter innan Invoke körs
            // Ignorera anrop om propertyName är null
            if (propertyName != null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        // Hjälpmetod för att sätta värdet på ett fält (storage) och trigga PropertyChanged
        // ENDAST om värdet faktiskt har ändrats. Detta undviker onödiga UI-uppdateringar.
        // propertyName skickas nu in explicit med nameof().
        protected bool SetProperty<T>(ref T storage, T value, string? propertyName) // <-- Ändrad signatur, ingen [CallerMemberNames], propertyName är inte default null längre
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName); // <-- Skicka propertyName här
            return true;
        }
    }
}