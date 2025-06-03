using System;
using System.Collections.ObjectModel; // Behövs för ObservableCollection
using System.ComponentModel; // Behövs för INotifyPropertyChanged
//using System.Runtime.CompilerServices; // *** TA BORT DENNA ***
using System.Threading.Tasks; // Behövs för Task och async/await
using System.Windows.Input; // Behövs för ICommand
using System.Windows.Threading; // Behövs för Dispatcher
using System.Windows; // Behövs för Application
using System.Linq; // Behövs för Any() och SingleOrDefault()
using System.Diagnostics; // Behövs för Debug.WriteLine

// Se till att dessa using-direktiv är korrekta baserat på DINA mappnamn
using BluetoothManager.Helpers; // Om ThemeManager ligger där, annars ta bort
using BluetoothManager.Services; // *** DENNA MÅSTE FINNAS *** För att hitta BluetoothService
using BluetoothManager.ViewModels; // *** DENNA MÅSTE FINNAS *** För BluetoothDeviceViewModel

using Windows.Devices.Enumeration; // Behövs för DeviceInformation och DeviceInformationUpdate


namespace BluetoothManager.ViewModels
{
    // Detta är ViewModel för huvudfönstret.
    // Den hanterar listan av enheter, temaväxling och interagerar med BluetoothService.
    // Den implementerar INotifyPropertyChanged för UI-bindningar
    // och IDisposable för att städa upp resurser (stoppa watchern).
    public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
    {
        // Referens till vår tjänst som pratar med Windows Bluetooth API.
        private readonly BluetoothService? _bluetoothService; // Markerad som nullable

        // Referens till UI-trådens Dispatcher. Behövs för att säkert uppdatera
        // ObservableCollection och andra UI-bundna egenskaper från bakgrundstrådar.
        private readonly Dispatcher _dispatcher;

        // ObservableCollection är en speciell typ av lista som notifierar UI:t
        // automatiskt när objekt läggs till, tas bort eller flyttas i listan.
        public ObservableCollection<BluetoothDeviceViewModel> Devices { get; } = new ObservableCollection<BluetoothDeviceViewModel>();

        // Egenskap för att styra om mörkt tema är aktivt. Binds till en ToggleButton i UI.
        private bool _isDarkTheme;
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            // Ändra SetProperty anropet till att använda nameof() explicit
            set
            {
                if (SetProperty(ref _isDarkTheme, value, nameof(IsDarkTheme))) // <-- Använd nameof() explicit
                {
                    // Om värdet ändrades, anropa vår ThemeManager för att byta tema.
                    // Denna anropas INTE längre här, ThemeManager sköter det globalt.
                    // ThemeManager.SetTheme(value ? ThemeManager.AppTheme.Dark : ThemeManager.AppTheme.Light); // <-- BORTTAGEN HÄR
                }
            }
        }

        // Egenskap för att visa statusmeddelanden längst ner i fönstret.
        private string _statusText = "Initialiserar..."; // Sätt initialt värde
        public string StatusText
        {
            get => _statusText;
            // Ändra SetProperty anropet till att använda nameof() explicit
            set => SetProperty(ref _statusText, value, nameof(StatusText)); // <-- Använd nameof() explicit
        }

        // Kommando för att ladda enheter. Används inte direkt i denna version
        // eftersom watchern startar automatiskt, men bra att ha som exempel.
        public ICommand LoadDevicesCommand { get; }

        // Kommando för att växla tema. Binds till en ToggleButton i UI.
        public ICommand ToggleThemeCommand { get; }


        // Konstruktorn för MainWindowViewModel. Körs när huvudfönstret skapas.
        public MainWindowViewModel()
        {
            Debug.WriteLine("MainWindowViewModel: Constructor called.");

            // Hämta Dispatcher för den *aktuella* tråden, vilket är UI-tråden här.
            // Använder Application.Current.Dispatcher för att vara säker på att få UI-trådens dispatcher i WPF.
            _dispatcher = Application.Current.Dispatcher;

            // Skapa en instans av vår BluetoothService.
            try
            {
                Debug.WriteLine("MainWindowViewModel: Creating BluetoothService...");
                _bluetoothService = new BluetoothService(); // Service tar inte emot Dispatcher längre i den uppdaterade koden
                Debug.WriteLine("MainWindowViewModel: BluetoothService created.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainWindowViewModel: ERROR creating BluetoothService: {ex.Message}");
                _bluetoothService = null; // Säkerställ att det är null om skapandet misslyckas
            }


            // Skapa kommandon som UI-element kan bindas till.
            // AsyncCommand och RelayCommand är enkla hjälpklasser för detta (vi lägger till dem strax om de inte redan finns i filen).
            LoadDevicesCommand = new AsyncCommand(LoadDevicesAsync);
            ToggleThemeCommand = new RelayCommand(() => IsDarkTheme = !IsDarkTheme); // Växlar bara värdet på IsDarkTheme

            // Sätt initialt tema. Detta hanteras nu i App.xaml.cs.
            // IsDarkTheme = false; // <-- BORTTAGEN HÄR

            // Prenumerera på events från BluetoothService.
            // Dessa events triggas när DeviceWatcher hittar/tar bort/uppdaterar enheter.
            if (_bluetoothService != null) // Lägg till null-check om _bluetoothService är nullable
            {
                Debug.WriteLine("MainWindowViewModel: Subscribing to BluetoothService events.");
                _bluetoothService.DeviceAdded += BluetoothService_DeviceAdded;
                _bluetoothService.DeviceRemoved += BluetoothService_DeviceRemoved;
                _bluetoothService.DeviceUpdated += BluetoothService_DeviceUpdated;
            }
            else
            {
                Debug.WriteLine("MainWindowViewModel: BluetoothService is null, cannot subscribe to events.");
            }


            // Starta DeviceWatcher direkt när ViewModel skapas för att börja hitta enheter.
            Debug.WriteLine("MainWindowViewModel: Calling _bluetoothService?.StartWatcher()...");
            _bluetoothService?.StartWatcher(); // Använd ?. för säker anrop om _bluetoothService är nullable
            StatusText = "Startar sökning efter parkopplade enheter..."; // Initial statusmeddelande
        }

        // Asynkron metod som kan användas för att ladda enheter (triggas av LoadDevicesCommand,
        // men watchern sköter det mesta nu).
        private async Task LoadDevicesAsync()
        {
            StatusText = "Söker efter parkopplade enheter...";

            // Rensa listan innan vi (eventuellt) laddar om.
            // Måste göras på UI-tråden eftersom Devices är bundet till UI.
            _dispatcher.Invoke(() =>
            {
                Debug.WriteLine("MainWindowViewModel: Clearing devices list.");
                // Rensa upp befintliga ViewModels innan vi rensar listan
                foreach (var vm in Devices)
                {
                    vm.Dispose();
                }
                Devices.Clear();
            });


            // I denna version, eftersom DeviceWatcher startar i konstruktorn
            // och triggar Added events, behöver vi inte göra något mer här
            // för att populera listan. Added-eventen sköter det.

            // Om man INTE använder watchern för initial laddning, kunde man
            // här anropat DeviceInformation.FindAllAsync(...) och lagt till
            // resultaten i Devices listan. Men watchern är bättre för löpande uppdateringar.

            // Ge watchern lite tid att hitta enheter och trigga "Added" events.
            // Detta är en HACK för att statusmeddelandet ska bli mer korrekt
            // direkt efter start. En bättre lösning vore att vänta på DeviceWatcher.EnumerationCompleted
            // eventet i Service och sedan trigga ett ViewModel event.
            await Task.Delay(1000); // Vänta 1 sekund
            _dispatcher.Invoke(() =>
            {
                StatusText = $"{Devices.Count} parkopplade enheter hittade.";
            });
        }

        // --- Event Handlers för events från BluetoothService ---
        // Dessa metoder anropas när BluetoothService triggar sina events.
        // VIKTIGT: Dessa anropas på bakgrundstrådar! Alla UI-uppdateringar
        // MÅSTE ske via _dispatcher.Invoke().

        private void BluetoothService_DeviceAdded(object sender, DeviceInformation deviceInfo)
        {
            Debug.WriteLine($"MainWindowViewModel: Device Added event received for {deviceInfo.Id} - {deviceInfo.Name}");
            // Kör på UI-tråden med Dispatcher.Invoke.
            _dispatcher.Invoke(() =>
            {
                // Hämta IsPaired egenskapen. Måste göras här eftersom den inte alltid finns i initial DeviceInformation.
                object isPairedValue;
                bool isPaired = false;
                if (deviceInfo.Properties.TryGetValue("System.Devices.Aep.IsPaired", out isPairedValue) && isPairedValue is bool)
                {
                    isPaired = (bool)isPairedValue;
                }

                // Kontrollera om vi redan har en ViewModel för denna enhet i listan OCH om den är parkopplad
                // Vi lägger endast till PARADE enheter i listan nu.
                if (isPaired && !Devices.Any(vm => vm.Id == deviceInfo.Id))
                {
                    Debug.WriteLine($"MainWindowViewModel: Adding paired device: {deviceInfo.Name}");
                    // Skapa en ny ViewModel för den hittade enheten.
                    // Skicka med den råa DeviceInformation och UI-trådens Dispatcher.
                    var vm = new BluetoothDeviceViewModel(deviceInfo, _dispatcher);
                    // Lägg till den nya ViewModel i ObservableCollection.
                    // Detta får UI:t att rita en ny rad i listan.
                    Devices.Add(vm);
                    Debug.WriteLine($"MainWindowViewModel: Added ViewModel for {vm.Name}");
                }
                else if (!isPaired)
                {
                    Debug.WriteLine($"MainWindowViewModel: Skipping non-paired device: {deviceInfo.Name}");
                }
                else
                {
                    // Enhet är parkopplad men redan i listan
                    Debug.WriteLine($"MainWindowViewModel: Device {deviceInfo.Id} already in list, skipping add.");
                }
                // Uppdatera statusmeddelandet.
                StatusText = $"{Devices.Count} parkopplade enheter hittade.";
            });
        }

        private void BluetoothService_DeviceRemoved(object sender, string deviceId)
        {
            Debug.WriteLine($"MainWindowViewModel: Device Removed event received for {deviceId}");
            // Kör på UI-tråden med Dispatcher.Invoke.
            _dispatcher.Invoke(() =>
            {
                // Hitta ViewModel för den borttagna enheten.
                var vmToRemove = FindDeviceViewModel(deviceId);
                if (vmToRemove != null)
                {
                    Debug.WriteLine($"MainWindowViewModel: Removing ViewModel for {vmToRemove.Name}");
                    // Anropa Dispose() för att rensa upp event-handlers och resurser
                    // inuti den borttagna ViewModel-instansen. VIKTIGT!
                    vmToRemove.Dispose();
                    // Ta bort ViewModel från ObservableCollection.
                    // Detta får UI:t att ta bort raden från listan.
                    Devices.Remove(vmToRemove);
                    Debug.WriteLine($"MainWindowViewModel: Removed ViewModel for {deviceId}");
                }
                // Uppdatera statusmeddelandet.
                StatusText = $"{Devices.Count} parkopplade enheter hittade.";
            });
        }

        private void BluetoothService_DeviceUpdated(object sender, DeviceInformationUpdate update)
        {
            Debug.WriteLine($"MainWindowViewModel: Device Updated event received for {update.Id}");
            // Kör på UI-tråden med Dispatcher.Invoke.
            _dispatcher.Invoke(() =>
            {
                // Hitta ViewModel för den uppdaterade enheten.
                var vmToUpdate = FindDeviceViewModel(update.Id);
                if (vmToUpdate != null)
                {
                    // Anropa Update-metoden på ViewModel för att den ska
                    // hantera uppdateringen (t.ex. namnbyte).
                    vmToUpdate.UpdateDeviceInfo(update);
                    Debug.WriteLine($"MainWindowViewModel: Updated ViewModel for {update.Id}");
                }
            });
        }

        // Hjälpmetod för att hitta en ViewModel i listan baserat på enhetens ID.
        private BluetoothDeviceViewModel? FindDeviceViewModel(string deviceId) // Markera returtyp som nullable
        {
            // Använd Linq's SingleOrDefault för att hitta en matchande ViewModel.
            // Om ingen eller fler än en hittas (vilket inte borde hända med ID som är unikt),
            // får vi null eller ett fel.
            return Devices.SingleOrDefault(vm => vm.Id == deviceId);
        }


        // --- Implementerar IDisposable ---
        // Denna metod kallas när MainWindowViewModel-objektet inte längre används,
        // t.ex. när huvudfönstret stängs. Vi måste städa upp våra resurser här.
        public void Dispose()
        {
            Debug.WriteLine("MainWindowViewModel: Dispose called.");

            // Stoppa BluetoothService's DeviceWatcher.
            Debug.WriteLine("MainWindowViewModel: Calling _bluetoothService?.StopWatcher()...");
            _bluetoothService?.StopWatcher(); // Använd ?. för säker anrop

            // Avprenumerera från BluetoothService events för att förhindra minnesläckor.
            if (_bluetoothService != null) // Lägg till null-check om _bluetoothService är nullable
            {
                Debug.WriteLine("MainWindowViewModel: Unsubscribing from BluetoothService events.");
                _bluetoothService.DeviceAdded -= BluetoothService_DeviceAdded;
                _bluetoothService.DeviceRemoved -= BluetoothService_DeviceRemoved;
                _bluetoothService.DeviceUpdated -= BluetoothService_DeviceUpdated;
            }


            // Anropa Dispose() på ALLA enskilda BluetoothDeviceViewModel-objekt
            // i listan för att de ska rensa upp sina resurser (t.ex. ConnectionStatusChanged event).
            Debug.WriteLine($"MainWindowViewModel: Disposing {Devices.Count} device ViewModels.");
            foreach (var vm in Devices)
            {
                vm.Dispose();
            }
            // Rensa listan.
            Devices.Clear(); // Detta kommer att notifiera UI:t att listan är tom.
            Debug.WriteLine("MainWindowViewModel: Devices list cleared.");


            // Se till att BluetoothService's Dispose kallas.
            Debug.WriteLine("MainWindowViewModel: Calling _bluetoothService as IDisposable)?.Dispose()...");
            (_bluetoothService as IDisposable)?.Dispose(); // Använd ?. för säker anrop och konvertering
                                                           // _bluetoothService = null; // Kan nollas om man vill vara extra tydlig
            Debug.WriteLine("MainWindowViewModel: Dispose completed.");
        }


        // --- Standard INotifyPropertyChanged Implementation ---
        // Samma standardkod som i BluetoothDeviceViewModel.
        public event PropertyChangedEventHandler? PropertyChanged; // Markerad som nullable event

        // Hjälpmetod för att trigga PropertyChanged-eventet.
        // propertyName skickas nu in explicit med nameof().
        protected virtual void OnPropertyChanged(string? propertyName) // <-- Ändrad signatur
        {
            // Kolla om PropertyChanged har några prenumeranter innan Invoke körs
            // Ignorera anrop om propertyName är null
            if (propertyName != null)
            {
                Debug.WriteLine($"MainWindowViewModel: PropertyChanged: {propertyName}");
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        // Hjälpmetod för att sätta värdet på ett fält (storage) och trigga PropertyChanged
        // ENDAST om värdet faktiskt har ändrats. Detta undviker onödiga UI-uppdateringar.
        // propertyName skickas nu in explicit med nameof().
        protected bool SetProperty<T>(ref T storage, T value, string? propertyName) // <-- Ändrad signatur
        {
            if (Equals(storage, value)) return false;
            storage = value;
            OnPropertyChanged(propertyName); // <-- Skicka propertyName här
            return true;
        }
    }

    // --- Hjälpklasser för ICommand Implementation ---
    // Dessa behövs för att binda metoder (som LoadDevicesAsync och ToggleThemeCommand)
    // till knappar och andra kontroller i XAML med kommandot "Command".
    // Lägg till dessa klasser NEDANFÖR MainWindowViewModel-klassen i SAMMA FIL
    // ELLER i separata filer i t.ex. Helpers-mappen och lägg till using där de behövs.
    // För enkelhetens skull ligger de här nu.

    // Enkel implementering för asynkrona metoder (som returnerar Task).
    // Observera att denna är FÖRENKLAD och saknar robust felhantering och CanExecute-logik
    // för en fullständig app.
    public class AsyncCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private bool _isExecuting; // Flagga för att undvika att kommandot körs igen medan det redan körs

        public AsyncCommand(Func<Task> execute)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        }

        // Event som triggas när CanExecute kan ha ändrats (t.ex. när _isExecuting ändras).
        public event EventHandler? CanExecuteChanged; // Markerad som nullable event

        // Bestämmer om kommandot kan köras just nu.
        public bool CanExecute(object? parameter) => !_isExecuting; // parameter kan vara null

        // Metoden som körs när kommandot anropas från UI:t.
        // Använder 'async void' vilket är OK för event handlers (som ICommand.Execute i WPF ofta är).
        // Observera att async void kan göra felhantering swigare.
        public async void Execute(object? parameter) // parameter kan vara null
        {
            if (CanExecute(parameter)) // Kolla om det är tillåtet att köra
            {
                try
                {
                    _isExecuting = true;
                    // Notifiera att CanExecute() nu kan returnera false.
                    // Använd ?. för säker anrop
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);

                    // Kör den asynkrona metoden.
                    await _execute();
                }
                catch (Exception ex)
                {
                    // Enkel felrapportering till output-fönstret
                    Debug.WriteLine($"AsyncCommand Error: {ex.Message}");
                    // I en riktig app skulle du visa detta för användaren.
                }
                finally
                {
                    // När metoden är klar (oavsett om det lyckades eller blev fel),
                    // återställ _isExecuting och notifiera att CanExecute() nu kan returnera true igen.
                    _isExecuting = false;
                    // Använd ?. för säker anrop
                    CanExecuteChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        // Metod för att manuellt trigga CanExecuteChanged (används ibland om andra faktorer
        // än _isExecuting påverkar om kommandot kan köras).
        protected void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty); // Använd ?.
        }
    }

    // Enkel implementering för synkrona metoder (som returnerar void).
    public class RelayCommand : ICommand
    {
        private readonly Action _execute; // Metoden som ska köras
        private readonly Func<bool>? _canExecute; // Valfri metod som bestämmer om kommandot kan köras (markerad som nullable)

        public RelayCommand(Action execute, Func<bool>? canExecute = null) // canExecute kan vara null
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute; // Sätt den valfria CanExecute-metoden
        }

        // Event som triggas när CanExecute kan ha ändrats.
        public event EventHandler? CanExecuteChanged; // Markerad som nullable event

        // Bestämmer om kommandot kan köras just nu. Anropar _canExecute om den finns, annars alltid true.
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(); // parameter kan vara null

        // Metoden som körs när kommandot anropas från UI:t.
        public void Execute(object? parameter) // parameter kan vara null
        {
            if (CanExecute(parameter)) // Kolla om det är tillåtet att köra
            {
                _execute();
            }
        }


        // Metod för att manuellt trigga CanExecuteChanged.
        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty); // Använd ?.
    }
}