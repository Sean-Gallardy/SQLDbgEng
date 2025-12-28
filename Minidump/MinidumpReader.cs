using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SQLDbgEng.Minidump
{
	public class MinidumpReader
	{
		public string FileName { get; private set; }
		public MINIDUMP_HEADER Header { get; private set; }
		public IEnumerable<MINIDUMP_DIRECTORY> Directories { get; private set; }
		public IEnumerable<string> Comments { get; private set; }
		public IEnumerable<MINIDUMP_THREAD> Threads { get; private set; }
		public IEnumerable<KeyValuePair<string,MINIDUMP_MODULE>> LoadedModules { get; private set; }
		public IEnumerable<KeyValuePair<string,MINIDUMP_UNLOADED_MODULE>> UnloadedModules { get; private set; }

		public MinidumpReader(){}

		public void ReadDump(string fileName)
		{
			if(!File.Exists(fileName))
			{
				throw new FileNotFoundException($"Can't open file {fileName} as it doesn't exist!", fileName);
			}

			FileName = fileName;

			BinaryReader br = new BinaryReader(File.OpenRead(fileName));
			Header = new MINIDUMP_HEADER(br);
			br.BaseStream.Position = Header.StreamDirectoryRva;

			List<MINIDUMP_DIRECTORY> directories = new List<MINIDUMP_DIRECTORY>();

			for (int i = 0; i < Header.NumberOfStreams; i++)
			{
				directories.Add(new MINIDUMP_DIRECTORY(br));
			}

			Directories = directories;

			ProcessDirectories(br);

			br.Close();
			br.Dispose();
		}

		private void ProcessDirectories(BinaryReader reader)
		{
			if (Directories is not null && Directories.Count() <= 0)
			{
				return;
			}

			List<string> comments = new List<string>();
			List<MINIDUMP_THREAD> threads = new List<MINIDUMP_THREAD>();
			List<KeyValuePair<string,MINIDUMP_MODULE>> loadedModules = new List<KeyValuePair<string, MINIDUMP_MODULE>>();
			List<KeyValuePair<string,MINIDUMP_UNLOADED_MODULE>> unloadedModules = new List<KeyValuePair<string, MINIDUMP_UNLOADED_MODULE>>();

			foreach(MINIDUMP_DIRECTORY d in Directories)
			{
				reader.BaseStream.Position = d.Location.Rva;

				switch (d.StreamType)
				{
					case (uint)MINIDUMP_STREAM_TYPE.CommentStreamW:
						comments.Add(Encoding.Unicode.GetString(reader.ReadBytes((int)d.Location.DataSize)));
						break;

					case (uint)MINIDUMP_STREAM_TYPE.ThreadListStream:
						int numThreads = (int)new MINIDUMP_THREAD_LIST(reader).NumberOfThreads;
						while(numThreads-- > 0)
						{
							threads.Add(new MINIDUMP_THREAD(reader));
						}
						break;

					case (uint)MINIDUMP_STREAM_TYPE.ModuleListStream:
						int numLoadedodules = (int) new MINIDUMP_MODULE_LIST(reader).NumberOfModules;
						while(numLoadedodules-- > 0)
						{
							MINIDUMP_MODULE loadedMod = new MINIDUMP_MODULE(reader);
							loadedModules.Add(new KeyValuePair<string, MINIDUMP_MODULE>(ReadStringFromRVA(loadedMod.ModuleNameRva, reader), loadedMod));
						}
						break;

					case (uint)MINIDUMP_STREAM_TYPE.UnloadedModuleListStream:
						int numUnloadedModules = (int)new MINIDUMP_UNLOADED_MODULE_LIST(reader).NumberOfEntries;
						while (numUnloadedModules-- > 0)
						{
							MINIDUMP_UNLOADED_MODULE unloadedMod = new MINIDUMP_UNLOADED_MODULE(reader);
							unloadedModules.Add(new KeyValuePair<string, MINIDUMP_UNLOADED_MODULE>(ReadStringFromRVA(unloadedMod.ModuleNameRva, reader), unloadedMod));
						}
						break;

					case (uint)

					default:
						break;
				}
			}

			Comments = comments;
			Threads = threads;
			LoadedModules = loadedModules;
			UnloadedModules = unloadedModules;
		}

		private string ReadStringFromRVA(UInt32 rva, BinaryReader reader)
		{
			long currentLocation = reader.BaseStream.Position;
			reader.BaseStream.Position = rva;
			UInt32 size = reader.ReadUInt32();
			string name = Encoding.Unicode.GetString(reader.ReadBytes((int)size));
			reader.BaseStream.Position = currentLocation;
			return name;
		}
	}
}
