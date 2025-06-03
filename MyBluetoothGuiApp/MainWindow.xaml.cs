// MainWindow.xaml.cs
using System; // Behövs för EventArgs, IDisposable
using System.Windows; // Behövs för Window

// Se till att denna using-direktiv är korrekt baserad på DINA mappnamn
using BluetoothManager.ViewModels; // För MainWindowViewModel

namespace BluetoothManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    // Implementerar IDisposable för att kunna kalla Dispose på ViewModel
    public partial class MainWindow : Window, IDisposable
    {
        // En referens till vår ViewModel för fönstret.
        private MainWindowViewModel _viewModel;

        // Konstruktorn. Körs när fönstret skapas.
        public MainWindow()
        {
            InitializeComponent(); // Standardmetod som laddar XAML och kopplar events/element.

            // Skapa en instans av vår MainWindowViewModel.
            _viewModel = new MainWindowViewModel();

            // Sätt fönstrets DataContext till vår ViewModel.
            // Detta är KOPPLINGEN mellan XAML och ViewModel.
            // Nu kan alla Binding-uttryck i XAML leta efter egenskaper och kommandon
            // i _viewModel-objektet.
            DataContext = _viewModel;

            // Prenumerera på fönstrets Closed event.
            // När fönstret stängs vill vi anropa Dispose() på ViewModel för att städa upp.
            this.Closed += MainWindow_Closed;
        }

        // Event handler för när fönstret stängs.
        private void MainWindow_Closed(object sender, EventArgs e)
        {
            // Anropa Dispose() på ViewModel för att stoppa BluetoothWatcher och städa upp resurser.
            Dispose(); // Kallar vår egen Dispose-metod
        }

        // Implementerar IDisposable för att städa upp.
        // Kallas både från MainWindow_Closed och om någon annan anropar Dispose på fönstret.
        public void Dispose()
        {
            // Anropa Dispose på ViewModel om den finns.
            _viewModel?.Dispose();

            // Avprenumerera från fönstrets Closed event för att undvika minnesläckor.
            this.Closed -= MainWindow_Closed;
        }
    }
}