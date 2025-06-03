using System;
using System.Collections.Generic; // Behövs för IEnumerable
using System.Linq; // Behövs för Linq
using System.Threading.Tasks; // Behövs för Task
using System.Windows.Threading; // Behövs för Dispatcher i WPF i nyare .NET
using Windows.Devices.Enumeration; // Behövs för DeviceInformation, DeviceWatcher, etc.
using Windows.Devices.Bluetooth; // Behövs för BluetoothDevice, etc.
using Windows.Foundation; // Behövs för IAsyncOperation
using System.Diagnostics; // Behövs för Debug.WriteLine

namespace BluetoothManager.Services
{
    // Denna klass är ansvarig för att söka efter och övervaka parkopplade Bluetooth-enheter
    // genom att använda Windows.Devices.Enumeration API (DeviceWatcher).
    public class BluetoothService : IDisposable
    {
        // Initialiseras till null. Watchern skapas i StartWatcher().
        private DeviceWatcher? _deviceWatcher = null; // Objektet som lyssnar på enhetsändringar i Windows (markerad som nullable)

        // Detta är ett "Advanced Query Syntax" (AQS) filter.
        // Det söker nu endast efter enheter med Bluetooth-protokollet.
        // Kravet på "IsPaired" har tagits bort från filtret för att undvika "Felaktig parameter" på vissa system.
        private const string BluetoothAqsFilter =
             "System.Devices.Aep.ProtocolId:=\"{e0ae54ac-77a4-463e-adeb-c03ad04f7576}\""; // Endast protokoll-ID för Bluetooth

        // Dessa är extra egenskaper (properties) vi vill att Windows ska inkludera
        // när den hittar eller uppdaterar information om en enhet.
        // Vi har tagit bort "System.Devices.Aep.Bluetooth.ClassOfDevice" som orsakade fel.
        // IsPaired behålls så att vi kan filtrera i ViewModel.
        private readonly string[] _requestedProperties =
        {
            "System.ItemNameDisplay", // Enhetens visningsnamn
            "System.Devices.Aep.IsPaired" // Om enheten är parkopplad (används för filtrering i ViewModel)
            // "System.Devices.Aep.Bluetooth.ClassOfDevice" - BORTTAGEN! Orsakade "Property key syntax error".
        };

        // Dessa är events (händelser) som andra delar av programmet (t.ex. vår ViewModel)
        // kan prenumerera på. När watchern hittar enheter, triggar vi dessa events.
        // Observera: Dessa events triggas på en bakgrundstråd, INTE UI-tråden.
        // Markerade som nullable event för att undvika nullability warnings.
        public event EventHandler<DeviceInformation>? DeviceAdded;
        public event EventHandler<string>? DeviceRemoved; // Skickar bara Id för borttagen enhet
        public event EventHandler<DeviceInformationUpdate>? DeviceUpdated;

        // Konstruktorn för BluetoothService. Den skapar INTE DeviceWatcher direkt längre.
        public BluetoothService()
        {
            // Watchern skapas och konfigureras i StartWatcher() metoden nu.
            Debug.WriteLine("BluetoothService: Constructor called.");
        }

        // Publik metod för att starta DeviceWatcher.
        // Denna metod skapar watchern (om den inte finns) och startar den.
        public void StartWatcher()
        {
            Debug.WriteLine($"BluetoothService: StartWatcher called. Current status: {_deviceWatcher?.Status.ToString() ?? "null"}");

            // Kontrollera om watchern är null eller om dess status indikerar att den inte är igång
            if (_deviceWatcher == null ||
                (_deviceWatcher.Status != DeviceWatcherStatus.Started &&
                 _deviceWatcher.Status != DeviceWatcherStatus.EnumerationCompleted))
            {
                try // Lägg till try-block för att fånga undantag
                {
                    // Om watchern inte finns, skapa den först och lägg till event handlers
                    if (_deviceWatcher == null)
                    {
                        Debug.WriteLine("BluetoothService: Creating DeviceWatcher...");
                        _deviceWatcher = DeviceInformation.CreateWatcher(
                             BluetoothAqsFilter, // Vårt filter för att hitta Bluetooth-enheter (nu mindre strikt)
                             _requestedProperties, // Vilka extra egenskaper vi vill ha
                             DeviceInformationKind.Device); // Vi letar efter allmänna enheter

                        // Lägg till event handlers NÄR watchern skapas
                        _deviceWatcher.Added += DeviceWatcher_Added;
                        _deviceWatcher.Removed += DeviceWatcher_Removed;
                        _deviceWatcher.Updated += DeviceWatcher_Updated;
                        _deviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
                        _deviceWatcher.Stopped += DeviceWatcher_Stopped;
                        Debug.WriteLine("BluetoothService: DeviceWatcher created and event handlers added.");
                    }

                    // Starta watchern om den inte redan är igång
                    if (_deviceWatcher.Status != DeviceWatcherStatus.Started &&
                        _deviceWatcher.Status != DeviceWatcherStatus.EnumerationCompleted)
                    {
                        Debug.WriteLine("BluetoothService: Starting DeviceWatcher...");
                        _deviceWatcher.Start(); // Startar sökningen
                        Debug.WriteLine("BluetoothService: DeviceWatcher started successfully.");
                    }
                    else
                    {
                        Debug.WriteLine($"BluetoothService: DeviceWatcher already in desired state: {_deviceWatcher.Status}");
                    }
                }
                catch (Exception ex) // Fånga undantaget
                {
                    // Logga felet. Detta kommer att fånga "Det gick inte att hitta elementet."
                    // eller "Felaktig parameter".
                    Debug.WriteLine($"BluetoothService: ERROR creating or starting watcher: {ex.Message}");
                    // HÄR kan du lägga till kod för att meddela ViewModel att ett fel inträffade,
                    // t.ex. via en ny event i BluetoothService: public event EventHandler<string> ErrorOccurred;
                    // ErrorOccurred?.Invoke(this, $"Kunde inte starta Bluetooth-övervakning: {ex.Message}");
                }
            }
            else
            {
                Debug.WriteLine($"BluetoothService: Watcher is already running or completed: {_deviceWatcher.Status}");
            }
        }

        // Publik metod för att stoppa DeviceWatcher.
        // Detta är viktigt att göra när applikationen avslutas för att frigöra resurser.
        public void StopWatcher()
        {
            Debug.WriteLine($"BluetoothService: StopWatcher called. Current status: {_deviceWatcher?.Status.ToString() ?? "null"}");

            // Vi stoppar bara watchern om den inte är stoppad eller i ett tillstånd där Stop() är tillåtet
            if (_deviceWatcher != null &&
                _deviceWatcher.Status != DeviceWatcherStatus.Stopped &&
                _deviceWatcher.Status != DeviceWatcherStatus.Created && // Stop() kan inte anropas på Created
                _deviceWatcher.Status != DeviceWatcherStatus.Aborted) // Stop() kan inte anropas på Aborted
            {
                try
                {
                    Debug.WriteLine("BluetoothService: Stopping DeviceWatcher...");
                    _deviceWatcher.Stop(); // Stoppar sökningen
                    Debug.WriteLine("BluetoothService: DeviceWatcher stopped successfully.");
                }
                catch (Exception ex)
                {
                    // Fånga eventuella fel här, t.ex. om Stop() anropas i fel tillstånd (mindre troligt med status-kontrollerna)
                    Debug.WriteLine($"BluetoothService: ERROR stopping watcher: {ex.Message}");
                }
            }
            else if (_deviceWatcher == null)
            {
                Debug.WriteLine("BluetoothService: Watcher is null, nothing to stop.");
            }
            else
            {
                Debug.WriteLine($"BluetoothService: Watcher status does not allow Stop(): {_deviceWatcher.Status}");
            }
        }


        // --- Privata metoder som hanterar events från DeviceWatcher ---
        // Dessa metoder anropas AV Windows när en händelse inträffar i DeviceWatcher.
        // De körs på en bakgrundstråd.

        private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args)
        {
            Debug.WriteLine($"BluetoothService: Device Added event: {args.Id} - {args.Name}");
            // Nu triggar vi vår EGEN 'DeviceAdded' event så att de som lyssnar
            // (vår ViewModel) får veta att en ny enhet hittades.
            // Vi skickar med den DeviceInformation som Windows gav us.
            // Observera: Denna event triggas på en bakgrundstråd! ViewModel måste använda Dispatcher.
            DeviceAdded?.Invoke(this, args);
        }

        private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            Debug.WriteLine($"BluetoothService: Device Removed event: {args.Id}");
            // Triggar vår EGEN 'DeviceRemoved' event. Vi skickar bara enhetens ID.
            // Observera: Denna event triggas på en bakgrundstråd! ViewModel måste använda Dispatcher.
            DeviceRemoved?.Invoke(this, args.Id);
        }

        private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args)
        {
            Debug.WriteLine($"BluetoothService: Device Updated event: {args.Id}");
            // Triggar vår EGEN 'DeviceUpdated' event. Vi skickar med uppdateringsinformationen.
            // Notera igen: ConnectionStatus finns *inte* direkt här. Statusändringar hanteras av
            // den enskilda enhetens ViewModel genom att hämta ett BluetoothDevice objekt.
            // Observera: Denna event triggas på en bakgrundstråd! ViewModel måste använda Dispatcher.
            DeviceUpdated?.Invoke(this, args);
        }

        private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args)
        {
            Debug.WriteLine("BluetoothService: DeviceWatcher enumeration completed.");
            // Kan användas för att t.ex. uppdatera en status i UI att den initiala listan är laddad.
            // Observera: Denna event triggas på en bakgrundstråd!
        }

        private void DeviceWatcher_Stopped(DeviceWatcher sender, object args)
        {
            Debug.WriteLine("BluetoothService: DeviceWatcher stopped.");
            // Observera: Denna event triggas på en bakgrundstråd!
        }


        // Metod för att rensa upp resurser när BluetoothService-objektet inte längre behövs.
        // Detta är en del av IDisposable-mönstret.
        public void Dispose()
        {
            Debug.WriteLine("BluetoothService: Disposing...");
            // Först, stoppa watchern.
            StopWatcher();

            // Sedan, om watchern finns, avprenumerera från alla dess events.
            // Detta är mycket viktigt för att förhindra minnesläckor.
            if (_deviceWatcher != null)
            {
                // Avprenumerera ALLA event handlers
                _deviceWatcher.Added -= DeviceWatcher_Added;
                _deviceWatcher.Removed -= DeviceWatcher_Removed;
                _deviceWatcher.Updated -= DeviceWatcher_Updated;
                _deviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;
                _deviceWatcher.Stopped -= DeviceWatcher_Stopped;

                // Släpp referensen till watchern.
                _deviceWatcher = null; // Nolla referensen
                Debug.WriteLine("BluetoothService: DeviceWatcher disposed and references cleared.");
            }
        }
    }
}