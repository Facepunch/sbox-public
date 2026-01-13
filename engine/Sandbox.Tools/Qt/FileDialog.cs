using System;

namespace Editor
{
	public class FileDialog : Widget
	{
		internal QFileDialog _filedialog;


		public FileDialog( Widget parent ) : base( false )
		{
			var ptr = QFileDialog.Create( parent?._widget ?? default );
			NativeInit( ptr );
		}

		internal override void NativeInit( IntPtr ptr )
		{
			_filedialog = ptr;

			base.NativeInit( ptr );
		}

		internal override void NativeShutdown()
		{
			_filedialog = default;

			base.NativeShutdown();
		}

		public bool Execute()
		{
			return _filedialog.exec() != 0;
		}

		public string Title
		{
			get => _filedialog.windowTitle();
			set
			{
				_filedialog.setWindowTitle( value );
			}
		}

		public string SelectedFile
		{
			get
			{
				using var files = _filedialog.selectedFiles();
				if ( files.size() == 0 ) return null;

				return files.at( 0 );
			}
		}

		public string Directory
		{
			get => _filedialog.directory();
			set => _filedialog.setDirectory( value );
		}

		public string DefaultSuffix
		{
			get => _filedialog.defaultSuffix();
			set => _filedialog.setDefaultSuffix( value );
		}

		public List<string> SelectedFiles
		{
			get
			{
				using var selectedFiles = _filedialog.selectedFiles();
				return selectedFiles.ToList();
			}
		}

		public void SetNameFilter( string text )
		{
			_filedialog.setNameFilter( text );
		}

		public void SetFindDirectory() => _filedialog.setFileMode( FileMode.Directory );
		public void SetFindFile() => _filedialog.setFileMode( FileMode.AnyFile );
		public void SetFindExistingFile() => _filedialog.setFileMode( FileMode.ExistingFile );
		public void SetFindExistingFiles() => _filedialog.setFileMode( FileMode.ExistingFiles );

		public void SetModeOpen()
		{
			_filedialog.setAcceptMode( AcceptMode.AcceptOpen );
		}

		public void SetModeSave()
		{
			_filedialog.setAcceptMode( AcceptMode.AcceptSave );
		}

		public void SelectFile( string file )
		{
			_filedialog.selectFile( file );
		}

		internal enum ViewMode { Detail, List }
		internal enum FileMode { AnyFile, ExistingFile, Directory, ExistingFiles }
		internal enum AcceptMode { AcceptOpen, AcceptSave }
		internal enum DialogLabel { LookIn, FileName, FileType, Accept, Reject }
		internal enum Option
		{
			ShowDirsOnly = 0x00000001,
			DontResolveSymlinks = 0x00000002,
			DontConfirmOverwrite = 0x00000004,
			DontUseNativeDialog = 0x00000010,
			ReadOnly = 0x00000020,
			HideNameFilterDetails = 0x00000040,
			DontUseCustomDirectoryIcons = 0x00000080
		};

	}
}
