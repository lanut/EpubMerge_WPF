using System.Collections.ObjectModel;
using EpubMerge.Gui;

// Presentation stays platform-neutral; generate its WinRT collection adapters in the host.
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(ObservableCollection<BookFile>))]
[assembly: WinRT.GeneratedWinRTExposedExternalType(typeof(BookFile))]
