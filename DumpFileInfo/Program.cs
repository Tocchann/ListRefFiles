// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Reflection;

if( args.Length < 1 )
{
	Usage();
	return 1;
}
foreach( var arg in args )
{
	if( Directory.Exists( arg ) )
	{
		WriteLine( arg );
		WriteLine( "" );
		DumpDirectory( arg, -1 );
	}
	else if( File.Exists( arg ) )
	{
		DumpFile( arg, -1 );
	}
}
return 0;
void Usage()
{
	var fileName = Path.GetFileNameWithoutExtension( Assembly.GetEntryAssembly()?.Location );
	Console.WriteLine( $"{fileName} <ファイル情報を表示するフォルダ>");
}
void WriteLine( string line )
{
	Console.WriteLine( line );
	Trace.WriteLine( line );
}
void DumpDirectory( string targetDir, int cutLength )
{
	if( cutLength < 0 )
	{
		cutLength = targetDir.Length;
		if( !targetDir.EndsWith( Path.DirectorySeparatorChar ) )
		{
			cutLength++;
		}
	}
	var subDirs = Directory.EnumerateDirectories( targetDir );
	foreach( var subDir in subDirs )
	{
		DumpDirectory( subDir, cutLength );
	}
	var files = Directory.EnumerateFiles( targetDir );
	foreach( var file in files )
	{
		DumpFile( file, cutLength );
	}
}
void DumpFile( string filePath, int cutLength )
{
	var fileName = cutLength < 0 ? Path.GetFileName( filePath ) : filePath.Substring( cutLength );
	var fileInfo = new FileInfo( filePath );
	var verInfo = FileVersionInfo.GetVersionInfo( filePath );
	if( verInfo.FileMajorPart != 0 || verInfo.FileMinorPart != 0 || verInfo.FileBuildPart != 0 || verInfo.FilePrivatePart != 0 )
	{
		WriteLine( $"{fileName}\t{fileInfo.LastWriteTime:s}\t{fileInfo.Length:N0}\t{fileInfo.Attributes}\t{verInfo.FileMajorPart}.{verInfo.FileMinorPart}.{verInfo.FileBuildPart}.{verInfo.FilePrivatePart}" );
	}
	else
	{
		WriteLine( $"{fileName}\t{fileInfo.LastWriteTime:s}\t{fileInfo.Length:N0}\t{fileInfo.Attributes}" );
	}
}
