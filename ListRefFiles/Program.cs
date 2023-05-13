// See https://aka.ms/new-console-template for more information

using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Serialization;

if( args.Length < 1 )
{
	Usage();
	return;
}
var xmlFiles = new List<string>();
var outputBase = string.Empty;
char prevParam = '\0';
bool separate = false;
foreach( var arg in args )
{
	if( arg[0] == '/' || arg[0] == '-' )
	{
		prevParam = arg[1];
	}
	else if( prevParam != '\0' )
	{
		switch( prevParam )
		{
			case 'o':
				outputBase = Path.GetFullPath( arg );
				break;
			case 's':
				separate = true;
				break;
		}
		prevParam = '\0';
	}
	else
	{
		xmlFiles.Add( Path.GetFullPath( arg ) );
	}
}
if( !string.IsNullOrEmpty(outputBase) && Directory.Exists( outputBase ) )
{
	Directory.Delete( outputBase, true );
}
foreach( var xmlPath in xmlFiles )
{
	ListupRefFiles( xmlPath, outputBase, separate );
}

void WriteLine( string value )
{
	Trace.WriteLine( value );
	Console.WriteLine( value );
}

void Usage()
{
	var fileName = Path.GetFileNameWithoutExtension( Assembly.GetEntryAssembly()?.Location );
	Console.WriteLine( $"{fileName} <ismファイルパス(XML形式のみ)> -o <コピー先フォルダ>" );
}

void ListupRefFiles( string xmlPath, string outputBase, bool separate )
{
	WriteLine( "" );
	WriteLine( $"ISMファイル:{xmlPath}" );
	var baseFolder = Path.GetDirectoryName( xmlPath )??"";
	var fileNode = Path.GetFileNameWithoutExtension( xmlPath );
	var pathVariables = new Dictionary<string, string>();   // パス名変換用テーブル
	var refFiles = new List<string>();	// ファイル参照するテーブル内のファイルパス一覧
	using( var streamReader = new StreamReader( xmlPath ) )
	{
		var settings = new XmlReaderSettings
		{
			DtdProcessing = DtdProcessing.Ignore,
		};
		using( var xmlReader = XmlReader.Create( streamReader, settings ) )
		{
			while( xmlReader.Read() )
			{
				if( xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "table" )
				{
					var tableName = xmlReader.GetAttribute( "name" );
					Console.WriteLine( $"Reading...{tableName}" );
					// パス変数 を取得
					if( tableName == "ISPathVariable" )
					{
						ReadPathVariable( xmlReader.ReadSubtree(), pathVariables, baseFolder, fileNode );
					}
					// ソースパス、ソースディレクトリを持つものを列挙(物理エントリーを探すだけ)
					else
					{
						ReadRefFile( xmlReader.ReadSubtree(), tableName, refFiles );
					}
				}
			}
		}
	}
	WriteLine( "" );
	WriteLine( "パス変数" );
	foreach( var kv in pathVariables )
	{
		WriteLine( $"{kv.Key}={kv.Value}" );
	}
	// 実在ファイルチェック
	var notExists = new List<string>();
	var existFiles = new List<string>();
	var copyFiles = new List<string>();
	var orgDir = Directory.GetCurrentDirectory();
	foreach( var file in refFiles )
	{
		var realPath = GetRealPath( file, pathVariables );
		//WriteLine( realPath );
		//WriteLine( file );
		//WriteLine( realPath );
		// 存在するファイル
		if( File.Exists( realPath ) )
		{
			existFiles.Add( realPath );
			copyFiles.Add( file );
		}
		// ディレクトリではない場合のみ見つからないファイル一覧に載せる
		else if( !Directory.Exists( realPath ) )
		{
			notExists.Add( realPath );
		}
	}
	Directory.SetCurrentDirectory( orgDir );
	WriteLine( "" );
	WriteLine( "以下のファイルまたはディレクトリが見つかりません。" );
	foreach( var file in notExists )
	{
		WriteLine( file );
	}
	WriteLine( "" );
	if( string.IsNullOrEmpty( outputBase ) )
	{
		WriteLine( "以下のファイルが参照されています。" );
		foreach( var file in existFiles.Order() )
		{
			WriteLine( file );
		}
	}
	else
	{
		WriteLine( "ファイルをコピーしています。" );
		var outputFolder = separate ? Path.Combine( outputBase, Path.GetFileNameWithoutExtension( xmlPath ) ) : outputBase;
		Directory.CreateDirectory( outputFolder );
		int skipLength = 0;
		if( pathVariables.TryGetValue( "ISProjectFolder", out var folder ) )
		{
			skipLength = folder.Length+1;
		}
		using( var hashAlgorithm = SHA256.Create() )
		{
			foreach( var file in copyFiles.Order() )
			{
				CopyFile( outputFolder, file, pathVariables, skipLength, hashAlgorithm );
			}
		}
	}
}

void CopyFile( string outputFolder, string file, Dictionary<string, string> pathVariables, int skipLength, SHA256 hashAlgorithm )
{
	var srcPath = GetRealPath( file, pathVariables );
	if( file.Contains( "<ISProjectFolder>" ) )
	{
		var dstPath = Path.Combine( outputFolder, srcPath.Substring( skipLength ) );
		var dstDir = Path.GetDirectoryName( dstPath );
		if( !string.IsNullOrEmpty( dstDir ) )
		{
			Directory.CreateDirectory( dstDir );
		}
		WriteLine( $"{srcPath} -> {dstPath}" );
		if( !File.Exists( dstPath ) )
		{
			File.Copy( srcPath, dstPath, true );
		}
		// 既にファイルが存在する場合、異なるファイルだったら、大問題なのでそれだけチェックする
		else
		{
			var srcHash = GenerateHashValue( hashAlgorithm, srcPath );
			var dstHash = GenerateHashValue( hashAlgorithm, dstPath );
			if( srcHash != dstHash )
			{
				WriteLine( $"{srcHash}とは別のファイルがすでにコピーされています" );
				throw new Exception( $"{srcHash}とは別のファイルがすでにコピーされています" );
			}
		}
	}
	else
	{
		WriteLine( $"{srcPath}" );
	}
}

string GetRealPath( string refFile, Dictionary<string, string> pathVariables )
{
	var realPath = "";
	int open = refFile.IndexOf( "<" );
	if( open != -1 )
	{
		open++;
		int close = refFile.IndexOf( ">", open );
		if( close != -1 )
		{
			var key = refFile.Substring( open, close - open );
			if( pathVariables.TryGetValue( key, out var value ) )
			{
				realPath = value;
			}
			else
			{
				foreach( var kv in pathVariables )
				{
					if( string.Compare( kv.Key, key, true, CultureInfo.InvariantCulture ) == 0 )
					{
						realPath = kv.Value;
						break;
					}
				}
			}
			refFile = refFile.Substring( close + 1 );
		}
	}
	if( !string.IsNullOrEmpty( refFile ) )
	{
		if( !string.IsNullOrEmpty( realPath ) && refFile[0] == '\\' )
		{
			refFile = refFile.Substring( 1 );
		}
		realPath = Path.Combine( realPath, refFile );
	}
	if( File.Exists( realPath ) )
	{
		var pathDir = Path.GetDirectoryName( realPath );
		if( pathDir != null )
		{
			var fileName = Path.GetFileName( realPath );
			var splitPath = pathDir.Split( Path.DirectorySeparatorChar );
			var newDir = string.Empty;
			var subDirs = Array.Empty<string>();
			foreach( var path in splitPath )
			{
				if( subDirs.Length > 0 )
				{
					var newPath = Path.Combine( newDir, path );
					newDir = subDirs.First( _ => string.Compare( _, newPath, true ) == 0 );
				}
				else
				{
					newDir = path;
					newDir += Path.DirectorySeparatorChar;    //	ルートだけ '\' がつかない
				}
				subDirs = Directory.GetDirectories( newDir );
			}
			var files = Directory.GetFiles( newDir );
			var findExistPath = files.First( _ => string.Compare( _, realPath, true ) == 0 );
			realPath = findExistPath;
		}
	}
	return realPath;
}

void ReadRefFile( XmlReader xmlReader, string? tableName, List<string> refFiles )
{
	var targetEntries = new List<int>();
	int index = -1;
	bool insideCol = false;
	while( xmlReader.Read() )
	{
		switch( xmlReader.NodeType )
		{
			case XmlNodeType.Element:
				switch( xmlReader.Name )
				{
					case "col":
						index++;
						insideCol = !xmlReader.IsEmptyElement;
						break;
					case "row":
						if( targetEntries.Count != 0 )
						{
							ReadRefFileTable( xmlReader.ReadSubtree(), refFiles, targetEntries );
						}
						break;
				}
				break;
			case XmlNodeType.EndElement:
				if( xmlReader.Name == "col" )
				{
					insideCol = false;
				}
				break;
			case XmlNodeType.Text:
				if( insideCol )
				{
					switch( xmlReader.Value )
					{
						case "ISBuildSourcePath":
						case "ISScriptFile":
						case "MsiPath":
							targetEntries.Add( index );
							break;
						case "SourceFolder":
							if( tableName == "ISDynamicFile" )
							{
								targetEntries.Add( index );
							}
							break;
					}
				}
				break;
		}
	}
}

void ReadRefFileTable( XmlReader xmlReader, List<string> refFiles, List<int> targetEntries )
{
	if( !xmlReader.Read() )
	{
		return;
	}
	Debug.Assert( xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "row" );
	bool insideTable = false;
	int currIndex = -1;
	while( xmlReader.Read() )
	{
		switch( xmlReader.NodeType )
		{
			case XmlNodeType.Element:
				if( xmlReader.Name == "td" )
				{
					currIndex++;
					insideTable = !xmlReader.IsEmptyElement;
				}
				break;
			case XmlNodeType.EndElement:
				if( xmlReader.Name == "td")
				{
					insideTable = false;
				}
				break;
			case XmlNodeType.Text:
				if( insideTable )
				{
					if( targetEntries.Contains( currIndex ) )
					{
						refFiles.Add( xmlReader.Value );
					}
				}
				break;
		}
	}
}

void ReadPathVariable( XmlReader xmlReader, Dictionary<string, string> pathVariables, string baseFolder, string fileNode )
{
	while( xmlReader.Read() )
	{
		if( xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "row" )
		{
			var tableData = ReadPathVariableTable( xmlReader.ReadSubtree() );
			Debug.Assert( tableData.Count == 4 );   //	常に４つのはず(colとか面倒なので見ない)

			var realPath = int.Parse( tableData[3] ) switch
			{
				1 => GetInstallShieldDefaultPath( tableData[0], baseFolder, fileNode ),
				2 => tableData[1],
				4 => Environment.GetEnvironmentVariable( tableData[1] ),
				8 => GetPathFromRegistry( tableData[1].Split( '\\' ) ),
				_ => throw new NotImplementedException(),
			};
			realPath ??= $"<{tableData[0]}>";	//	変換できない場合はそのままセット
			pathVariables.Add( tableData[0], realPath );
		}
	}
	if( !pathVariables.ContainsKey( "ISProductFolder" ) )
	{
		pathVariables.Add( "ISProductFolder", @"C:\Program Files (x86)\InstallShield\2022\" );
	}
	if( !pathVariables.ContainsKey( "ISRedistPlatformDependentFolder" ) )
	{
		pathVariables.Add( "ISRedistPlatformDependentFolder", @"C:\Program Files (x86)\InstallShield\2022\Redist\Language Independent\i386" );
	}
}
List<string> ReadPathVariableTable( XmlReader xmlReader )
{
	var tableData = new List<string>();
	// あり得ないけど一応…
	if( !xmlReader.Read() )
	{
		return tableData;
	}
	Debug.Assert( xmlReader.NodeType == XmlNodeType.Element && xmlReader.Name == "row" );

	bool insideTable = false;
	bool addText = false;
	while( xmlReader.Read() )
	{
		switch( xmlReader.NodeType )
		{
			case XmlNodeType.Element:
				if( xmlReader.Name == "td" )
				{
					insideTable = !xmlReader.IsEmptyElement;
					if( xmlReader.IsEmptyElement )
					{
						tableData.Add( "" );	//	空エントリーを追加しておく
					}
				}
				break;
			case XmlNodeType.EndElement:
				if( xmlReader.Name == "td" )
				{
					insideTable = false;
					// <td></td> で保存されていた場合でも誤動作しないようにしておく(そんなことはないはずだけど)
					if( !addText )
					{
						tableData.Add( "" );
					}
				}
				break;
			case XmlNodeType.Text:
				if( insideTable )
				{
					tableData.Add( xmlReader.Value );
					addText = true;
				}
				break;
		}
	}
	return tableData;
}

string? GetPathFromRegistry( string[] regPaths )
{
	if( regPaths.Length >= 3 )
	{
		var regKey = regPaths[0] switch
		{
			"HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
			"HKEY_CURRENT_USER" => Registry.CurrentUser,
			"HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
			"HKEY_USERS" => Registry.Users,
			_ => throw new NotSupportedException( $"RootKey={regPaths[0]}" )
		};
		for( int index = 1 ; index < regPaths.Length-1 ; index++ )
		{
			regKey = regKey?.OpenSubKey( regPaths[index] );
		}
		return regKey?.GetValue( regPaths[regPaths.Length - 1] ) as string;
	}
	return null;
}


string? GetInstallShieldDefaultPath( string name, string baseFolder, string fileNode )
{
	return name switch
	{
		"ISProductFolder" => @"C:\Program Files (x86)\InstallShield\2022\",
		"ISRedistPlatformDependentFolder" => @"C:\Program Files (x86)\InstallShield\2022\Redist\Language Independent\i386",
		"ISProjectFolder" => baseFolder,
		"ISProjectDataFolder" or "ISPROJECTDIR" => Path.Combine( baseFolder, fileNode ),
		"SystemFolder" => Environment.GetFolderPath( Environment.SpecialFolder.SystemX86 ),
		"CommonFilesFolder" => Environment.GetFolderPath( Environment.SpecialFolder.CommonProgramFilesX86 ),
		"ProgramFilesFolder" => Environment.GetFolderPath( Environment.SpecialFolder.ProgramFilesX86 ),
		"WindowsFolder" => Environment.GetFolderPath( Environment.SpecialFolder.Windows ),
		_ => "",
	};
	throw new NotImplementedException();
}

string GenerateHashValue( HashAlgorithm hashAlgorithm, string filePath )
{
	try
	{
		using( var stream = File.OpenRead( filePath ) )
		{
			var hashBytes = hashAlgorithm.ComputeHash( stream );
			//	ハッシュは、16進数値文字列化して一意キーとする(.NET5から追加されていたので変更)
			var result = Convert.ToHexString( hashBytes );
			return result;
		}
	}
	catch
	{
		Trace.WriteLine( "Fail:GenerateHashFileName({filePath})" );
		throw;
	}
}
