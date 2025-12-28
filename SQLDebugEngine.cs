using Microsoft.Diagnostics.Runtime.Interop;
using SQLDbgEng.Logging;
using SQLDbgEng.Minidump;
using System.Runtime.InteropServices;
using System.Text;

namespace SQLDbgEng
{
	/// <summary>
	/// A very basic implementation of utilizing DbgEng with C# for the purpose of obtaining typical data from a SQL Server memory dump.
	/// This is a monolithic implementation and was not made with extensibility or other items in mind. Merely it was made to obtain data
	/// from the dump files in various ways, such as via output capture from the debugger, using debugger api calls, and via reading the dump
	/// files directly and parsing them. No implementation has been done for debugger events.
	/// 
	/// Properties are lazy loaded as each item may or may not be used for different dumps.
	/// 
	/// SQL Server didn't always follow proper minidump rules, for example there should only be a single stream of each type. However, 
	/// in the dumps for SQL Server there are typically between 1 and 3 streams for UserCommentW. There is no public extension for 
	/// decompressing memory in dumps from SQL Server 2022 and above, however I did add a property for compressed memory segments.
	/// 
	/// There are no safety checks, if you don't open a dump successfully first, all of the calls will throw. You've been warned.
	/// 
	/// This comes with all of the baggage of DbgEng, for example, 1 open dump per process, syncronous processing, etc.
	/// 
	/// Official Symbols Server from MS has been included for ease of reference. Image path and Symbols path shsould be set accordingly.
	/// 
	/// This work is licensed under CC BY-NC-SA 4.0
	/// </summary>
	public class SQLDebugEngine
	{
		public static readonly string OfficialMicrosoftSymbolsServer = @"https://msdl.microsoft.com/download/symbols";

		public event EventHandler<DebuggerOutput> DebuggerOutputEvent;

		[DllImport("dbgeng.dll", CallingConvention = CallingConvention.StdCall, SetLastError = true)]
		private static extern int DebugCreate([In, MarshalAs(UnmanagedType.LPStruct)] Guid riid, [Out, MarshalAs(UnmanagedType.IUnknown)] out object ppvObject);

		private static readonly Guid s_DebugClient5Guid = new Guid("e3acb9d7-7ec2-4f0c-a0da-e81e0cbbe628");
		private static readonly Guid s_DebugAdvanced3Guid = new Guid("cba4abb4-84c4-444d-87ca-a04e13286739");
		private static readonly Guid s_DebugControl5Guid = new Guid("b2ffe162-2412-429f-8d1d-5bf6dd824696");
		private static readonly Guid s_DebugSymbols5Guid = new Guid("c65fa83e-1e69-475e-8e0e-b5d79e9cc17e");
		private static readonly Guid s_DataSpaces4Guid = new Guid("d98ada1f-29e9-4ef5-a6c0-e53349883212");
		private static readonly Guid s_SystemObjects3Guid = new Guid("e9676e2f-e286-4ea3-b0f9-dfe5d9fc330e");

		private const int S_OK = 0;
		private const int S_FALSE = 1;

		private IDebugClient5 _debugClient;
		private IDebugAdvanced3 _debugAdvanced;
		private IDebugControl5 _debugControl;
		private IDebugSymbols5 _debugSymbols;
		private IDebugDataSpaces4 _debugDataSpaces;
		private IDebugSystemObjects3 _debugSystemObjects;

		private MinidumpReader _reader;
		private DebugOutputManager m_debugOutputManager;

		private uint? m_dumpThreadID = null;
		private DateTime? m_dumpDateTime = null;
		private uint? m_processPID = null;
		private TimeSpan? m_processUpTime = null;
		private TimeSpan? m_systemUpTime = null;
		private string? m_commandLine = null;
		private Dictionary<string, string>? m_environmentVariables = null;
		private List<int>? m_traceFlags = null;
		private string? m_instanceName = null;
		private string? m_systemManufacturer = null;
		private string? m_systemProductName = null;
		private List<DumpThreadInfo>? m_threads = null;
		private bool? m_isMemoryCompressed = null;
		private List<DumpLoadedModuleInfo>? m_loadedModules = null;
		private List<DumpUnloadedModuleInfo>? m_unloadedModules = null;

		// Gives clients access because not everything is implemented or public
		public IDebugClient5 DebugClientInterface { get { return _debugClient; } }
		public IDebugAdvanced3 DebugAdvancedInterface { get { return _debugAdvanced; } }
		public IDebugControl5 DebugControlInterface { get { return _debugControl; } }
		public IDebugSymbols5 DebugSymbolsInterface { get { return _debugSymbols; } }
		public IDebugDataSpaces4 DebugDataSpacesInterface { get { return _debugDataSpaces; } }
		public IDebugSystemObjects3 DebugSystemObjectsInterface { get { return _debugSystemObjects; } }

		/// <summary>
		/// File being debugged.
		/// </summary>
		public string FileName { get; private set; }
		
		/// <summary>
		/// Symbols path set in the debugger.
		/// </summary>
		public string SymbolsPath { get; private set; }

		/// <summary>
		/// ImagePath set in the debugger.
		/// </summary>
		public string ImagePath { get; private set; }


		/// <summary>
		/// The debugger thread ID of the thread which caused the dump.
		/// </summary>
		public uint DumpThreadDebuggerID
		{
			get
			{
				if (m_dumpThreadID is null)
				{
					CheckReturnHRAndThrow(_debugSystemObjects.GetEventThread(out uint threadID));
					m_dumpThreadID = threadID;
				}

				return m_dumpThreadID.Value;
			}
		}

		/// <summary>
		/// Date and time the dump occurred.
		/// </summary>
		public DateTime DateAndTimeOfDump
		{
			get
			{
				if (m_dumpDateTime is null)
				{
					CheckReturnHRAndThrow(_debugControl.GetCurrentTimeDate(out uint unixTimestamp));
					m_dumpDateTime = GetDateTimeFromUnixTimestamp(unixTimestamp);
				}

				return m_dumpDateTime.Value;
			}
		}

		/// <summary>
		/// Windows PID of the SQL Server process.
		/// </summary>
		public uint ProcessPID
		{
			get
			{
				if (m_processPID is null)
				{
					CheckReturnHRAndThrow(_debugSystemObjects.GetCurrentProcessSystemId(out uint pid));
					m_processPID = pid;
				}

				return m_processPID.Value;
			}
		}

		/// <summary>
		/// Uptime of the SQL Server process when the dump occurred.
		/// </summary>
		public TimeSpan ProcessUptime
		{
			get
			{
				if (m_processUpTime is null)
				{
					CheckReturnHRAndThrow(_debugSystemObjects.GetCurrentProcessUpTime(out uint secondsOfUptime));
					m_processUpTime = TimeSpan.FromSeconds(secondsOfUptime);
				}

				return m_processUpTime.Value;
			}
		}

		/// <summary>
		/// Uptime of the Windows OS when the dump occurred.
		/// </summary>
		public TimeSpan SystemUptime
		{
			get
			{
				if (m_systemUpTime is null)
				{
					CheckReturnHRAndThrow(_debugControl.GetCurrentSystemUpTime(out uint secondsOfUptime));
					m_systemUpTime = TimeSpan.FromSeconds(secondsOfUptime);
				}

				return m_systemUpTime.Value;
			}
		}

		/// <summary>
		/// Command line used to start the SQL Server process.
		/// </summary>
		public string ProcessCommandLine
		{
			get
			{
				if (m_commandLine is null)
				{
					m_commandLine = GetProcessCommandLine();
				}

				return m_commandLine;
			}
		}

		/// <summary>
		/// Windows environment variables at the time of SQL Server start up.
		/// </summary>
		public Dictionary<string, string> EnvironmentVariables
		{
			get
			{
				if (m_environmentVariables is null)
				{
					m_environmentVariables = GetProcessEnvironmentVariables();
				}

				return m_environmentVariables;
			}
		}

		/// <summary>
		/// SQL Server trace flags found in the dump.
		/// </summary>
		public IEnumerable<int> TraceFlags
		{
			get
			{
				if (m_traceFlags is null)
				{
					m_traceFlags = GetTraceFlags();
				}

				return m_traceFlags;
			}
		}

		/// <summary>
		/// SQL Instance name, such as MSSQLSERVER or SQLInst2.
		/// </summary>
		public string SQLInstanceName
		{
			get
			{
				if (m_instanceName is null)
				{
					m_instanceName = GetSQLServerInstanceNameFromCommandLine(ProcessCommandLine);
				}

				return m_instanceName;
			}
		}

		/// <summary>
		/// System manufacturer such as Microsoft, Gigabyte, Amazon, etc.
		/// </summary>
		public string SystemManufacturer
		{
			get
			{
				if(m_systemManufacturer is null)
				{
					m_systemManufacturer = GetSystemManufacturer();
				}

				return m_systemManufacturer;
			}
		}

		/// <summary>
		/// System name such as Virtual Machine, R720, etc.
		/// </summary>
		public string SystemProductName
		{
			get
			{
				if(m_systemProductName is null)
				{
					m_systemProductName = GetSystemProductName();
				}

				return m_systemProductName;
			}
		}

		/// <summary>
		/// Number of processors on the machine in the dump.
		/// </summary>
		public uint NumberOfProcessors
		{
			get
			{
				string value = TryGetValueFromEnvironmentVariables("NUMBER_OF_PROCESSORS");
				if(!string.IsNullOrEmpty(value))
				{
					return uint.Parse(value);
				}

				return 0;
			}
		}

		/// <summary>
		/// Username portion of the service account, without the domain.
		/// </summary>
		public string ServiceAccountName
		{
			get
			{
				return TryGetValueFromEnvironmentVariables("USERNAME");
			}
		}

		/// <summary>
		/// Domain, if applicable for the service account.
		/// </summary>
		public string ServiceAccountDomain
		{
			get
			{
				return TryGetValueFromEnvironmentVariables("USERDOMAIN");
			}
		}

		/// <summary>
		/// The domain a computer belongs to, if applicable.
		/// </summary>
		public string ComputerDomainName
		{
			get
			{
				return TryGetValueFromEnvironmentVariables("USERDNSDOMAIN");
			}
		}

		/// <summary>
		/// The name of the computer the dump occurred.
		/// </summary>
		public string ComputerName
		{
			get
			{
				return TryGetValueFromEnvironmentVariables("COMPUTERNAME");
			}
		}

		/// <summary>
		/// List of threads in the dump.
		/// </summary>
		public IEnumerable<DumpThreadInfo> Threads
		{
			get
			{
				if(m_threads is null)
				{
					m_threads = GetThreads();
				}

				return m_threads;
			}
		}

		/// <summary>
		/// Shows if the memory is compressed for the SQL process or not.
		/// If the memory is compressed, you'll need to have the proper files
		/// for the debugger to decompress it, or run it through sqldumpr in 
		/// order to get the proper memory.
		/// </summary>
		public bool IsMemoryCompressed
		{
			get
			{
				if(m_isMemoryCompressed is null)
				{
					m_isMemoryCompressed = false;
					foreach(MINIDUMP_DIRECTORY d in _reader.Directories)
					{
						if(d.StreamType == (uint)MINIDUMP_STREAM_TYPE.SQLCompressedMemoryStream)
						{
							m_isMemoryCompressed = true;
							break;
						}
					}
				}

				return m_isMemoryCompressed.Value;
			}
		}

		/// <summary>
		/// List of loaded modules in the dump.
		/// </summary>
		public IEnumerable<DumpLoadedModuleInfo> LoadedModules
		{
			get
			{
				if(m_loadedModules is null)
				{
					m_loadedModules = GetLoadedModules();
				}

				return m_loadedModules;
			}
		}

		/// <summary>
		/// List of unlaoded modules in the dump.
		/// </summary>
		public IEnumerable<DumpUnloadedModuleInfo> UnloadedModules
		{
			get
			{
				if(m_unloadedModules is null)
				{
					m_unloadedModules = GetUnloadedModules();
				}

				return m_unloadedModules;
			}
		}

		public bool IsSQLServerDump
		{
			get
			{
				return LoadedModules.Any(mod => mod.ModuleName.Contains("sqlservr.exe", StringComparison.InvariantCultureIgnoreCase));
			}
		}

		public SQLDebugEngine()
		{
		}

		/// <summary>
		/// Starting point for working with the dumps.
		/// Does not check for already opened files or file existence.
		/// </summary>
		/// <param name="fileName"></param>
		public void OpenDump(string fileName)
		{
			LoadMinidumpReader(fileName);

			_debugClient = DebugCreateInterface<IDebugClient5>(s_DebugClient5Guid);
			_debugAdvanced = DebugCreateInterface<IDebugAdvanced3>(s_DebugAdvanced3Guid);
			_debugControl = DebugCreateInterface<IDebugControl5>(s_DebugControl5Guid);
			_debugSymbols = DebugCreateInterface<IDebugSymbols5>(s_DebugSymbols5Guid);
			_debugDataSpaces = DebugCreateInterface<IDebugDataSpaces4>(s_DataSpaces4Guid);
			_debugSystemObjects = DebugCreateInterface<IDebugSystemObjects3>(s_SystemObjects3Guid);

			FileName = fileName;

			m_debugOutputManager = new DebugOutputManager();
			m_debugOutputManager.DebuggerOutputEvent += OnDebuggerOutput;
			CheckReturnHRAndThrow(_debugClient.SetOutputCallbacksWide(m_debugOutputManager));

			CheckReturnHRAndThrow(_debugClient.OpenDumpFileWide(FileName, 0));
			CheckReturnHRAndThrow(_debugControl.WaitForEvent(DEBUG_WAIT.DEFAULT, uint.MaxValue));
		}

		private void OnDebuggerOutput(object? sender, DebuggerOutput e)
		{
			DebuggerOutputEvent?.Invoke(sender, e);
		}

		private void LoadMinidumpReader(string fileName)
		{
			_reader = new MinidumpReader();
			_reader.ReadDump(fileName);
		}

		/// <summary>
		/// Sets the symbols path in the debugger.
		/// If the symbols already exist (such as pointing to a shared symbols store)
		/// this will be helpful. Otherwise, it'll download and cache the symbol each time.
		/// This can be quite a large amount of space, using a persistent location is ideal.
		/// </summary>
		/// <param name="path"></param>
		public void SetSymbolsPath(string path)
		{
			CheckReturnHRAndThrow(_debugSymbols.SetSymbolPathWide(path));
			SymbolsPath = path;
		}

		/// <summary>
		/// Sets the image path in the debugger.
		/// If the file is not local to the server, may need to be manually set.
		/// Otherwise, not needed as file image locations will be identical.
		/// </summary>
		/// <param name="path"></param>
		public void SetImagePath(string path)
		{
			CheckReturnHRAndThrow(_debugSymbols.SetImagePathWide(path));
			ImagePath = path;
		}

		/// <summary>
		/// Executes a command in the debugger. Should be a valid debugger command.
		/// </summary>
		/// <param name="command"></param>
		public void ExecuteDebuggerCommand(string command)
		{
			CheckReturnHRAndThrow(_debugControl.ExecuteWide(DEBUG_OUTCTL.ALL_CLIENTS, command, DEBUG_EXECUTE.DEFAULT));
		}

		/// <summary>
		/// Executes a command in the debugger and captures the output specifically
		/// that occurs only while running the command.
		/// </summary>
		/// <param name="command"></param>
		/// <returns></returns>
		public string ExecuteDebuggerCommandAndCaptureOutput(string command)
		{
			DebuggerOutputSimpleCapture capturedOutput = new DebuggerOutputSimpleCapture();
			DebuggerOutputEvent += capturedOutput.Output;
			ExecuteDebuggerCommand(command);
			DebuggerOutputEvent -= capturedOutput.Output;
			return capturedOutput.CapturedDebuggerOutput;
		}

		/// <summary>
		/// Converts a unix timestamp (number of seconds since 1970) to a DateTime.
		/// </summary>
		/// <param name="unixTimestamp">number of seconds since the unix epoch.</param>
		/// <returns>DateTime</returns>
		private DateTime GetDateTimeFromUnixTimestamp(uint unixTimestamp)
		{
			return DateTime.UnixEpoch.AddSeconds(unixTimestamp);
		}

		/// <summary>
		/// Generates a generic exception from the supplied HResult.
		/// </summary>
		/// <param name="hr">HResult</param>
		/// <returns>Exception for the specific HR.</returns>
		private Exception GetExceptionFromCOMHResult(int hr)
		{
			Exception tempEx = Marshal.GetExceptionForHR(hr);

			if(tempEx != null)
			{
				return tempEx;
			}

			return new Exception() { HResult = hr };
		}

		/// <summary>
		/// Used to wrap the COM calls to DbgEng and throws if it's not a success.
		/// </summary>
		/// <param name="hr">HResult of the call</param>
		private void CheckReturnHRAndThrow(int hr)
		{
			if (hr != S_OK)
			{
				throw GetExceptionFromCOMHResult(hr);
			}
		}

		/// <summary>
		/// Creates the various Debug* interfaces required to interact with DebgEng.dll
		/// </summary>
		/// <typeparam name="T">Interface required</typeparam>
		/// <param name="comInterfaceGUID">GUID of required interface T</param>
		/// <returns>Interface as T</returns>
		private T DebugCreateInterface<T>(Guid comInterfaceGUID)
		{
			object debugObject;

			CheckReturnHRAndThrow(DebugCreate(comInterfaceGUID, out debugObject));

			T debugInterface = (T)debugObject;
			if (debugInterface != null)
			{
				return debugInterface;
			}
			else
			{
				throw new InvalidCastException($"Failed to cast to {typeof(T).Name} interface");
			}
		}

		/// <summary>
		/// Returns the virtual address of a symbol.
		/// </summary>
		/// <param name="symbolName">Name of the symbol, eg: "NT_PEB"</param>
		/// <returns>Address of supplied symbol.</returns>
		public ulong GetSymbolLocationByName(string symbolName)
		{
			CheckReturnHRAndThrow(_debugSymbols.GetOffsetByNameWide(symbolName, out ulong offset));
			return offset;
		}

		/// <summary>
		/// Returns the number of bytes a field is offset from the beginning of the structure.
		/// </summary>
		/// <param name="symbolName">Structure in the form of a symbol, ex: "NT!_PEB"</param>
		/// <param name="fieldName">Field inside of the structure that you want to get the offset, eg: "Environment"</param>
		/// <returns>The number of bytes into a base structure the field starts.</returns>
		public ulong GetFieldOffsetFromSymbolByName(string symbolName, string fieldName)
		{
			CheckReturnHRAndThrow(_debugSymbols.GetSymbolTypeIdWide(symbolName, out uint typeID, out ulong module));
			CheckReturnHRAndThrow(_debugSymbols.GetFieldTypeAndOffsetWide(module, typeID, fieldName, out uint fieldTypeID, out uint fieldOffset));

			return fieldOffset;
		}

		/// <summary>
		/// Reads an arbitrary array of bytes from virtual memory.
		/// </summary>
		/// <param name="memoryLocation">location of the memory to start reading.</param>
		/// <param name="bytesToRead">number of bytes to read from memory.</param>
		/// <returns>byte array of memory read</returns>
		/// <exception cref="InvalidDataException">Read bytes doesn't match bytesToRead.</exception>
		public byte[] ReadVirtualMemory(ulong memoryLocation, uint bytesToRead)
		{
			byte[] buffer = new byte[bytesToRead];
			using (AutoPinMemory bufferPin = new AutoPinMemory(buffer))
			{
				CheckReturnHRAndThrow(_debugDataSpaces.ReadVirtual(memoryLocation, buffer, bytesToRead, out uint bytesRead));
				if(bytesRead != bytesToRead)
				{
					throw new InvalidDataException($"Asked to read {bytesToRead} bytes but read {bytesRead} bytes.");
				}
			}

			return buffer;
		}

		/// <summary>
		/// Reads the memory from a specified address as sizeof(T) bytes and converts to T.
		/// </summary>
		/// <typeparam name="T">Type to convert the read data into and also the amount of data to read.</typeparam>
		/// <param name="memoryLocation">location of the memory to read</param>
		/// <returns>sizeof(T) data read from memory location converted to T</returns>
		/// <exception cref="InvalidDataException">Read bytes doesn't match sizeof(T) bytes.</exception>
		public T ReadVirtualMemory<T>(ulong memoryLocation) where T : struct
		{
			int size = Marshal.SizeOf(typeof(T));

			return ConvertBytes<T>(ReadVirtualMemory(memoryLocation, (uint)size));
		}

		/// <summary>
		/// Attempts to convert from a byte array to an integral type T.
		/// </summary>
		/// <typeparam name="T">Integral type to return.</typeparam>
		/// <param name="data">byte array data to convert.</param>
		/// <returns>Integral type T.</returns>
		/// <exception cref="NotImplementedException">Throws if Type conversion isn't implemented.</exception>
		private static T ConvertBytes<T>(byte[] data)
		{
			Type type = typeof(T);

			//BitConverter is slow, could be replaced with bitwise operations
			if (type == typeof(byte))
			{
				return (T)(object)data[0];
			}

			if (type == typeof(byte[]))
			{
				return (T)(object)data;
			}

			if(type == typeof(short))
			{
				return (T)(object)BitConverter.ToInt16(data);
			}

			if(type == typeof(ushort))
			{
				return (T)(object)BitConverter.ToUInt16(data);
			}

			if (type == typeof(Int32))
			{
				return (T)(object)BitConverter.ToInt32(data);
			}

			if(type == typeof(UInt32))
			{
				return (T)(object)BitConverter.ToUInt32(data);
			}

			if(type == typeof(Int64))
			{
				return (T)(object)BitConverter.ToInt64(data);
			}

			if (type == typeof(ulong))
			{
				return (T)(object)BitConverter.ToUInt64(data);
			}


			throw new NotImplementedException($"Type conversion to {type} has not been implemented.");
		}

		/// <summary>
		/// Reads a memory location and derefences.
		/// </summary>
		/// <param name="memoryLocation">memory location to read and derefence.</param>
		/// <returns>dereferenced value at memory location.</returns>
		public ulong ReadVirtualPointer(ulong memoryLocation)
		{
			ulong[] pointer = { 0 };
			using (AutoPinMemory pointerPin = new AutoPinMemory(pointer))
			{
				CheckReturnHRAndThrow(_debugDataSpaces.ReadPointersVirtual(1, memoryLocation, pointer));
			}

			return pointer[0];

		}

		/// <summary>
		/// Reads an NT Unicode string.
		/// NT Unicode strings have a size and maxsize prepended before the string data.
		/// </summary>
		/// <param name="memoryLocation">start location in memory of the _UNICODE_STRING structure.</param>
		/// <returns>string portion of the unicode string without length data.</returns>
		/// <exception cref="InvalidDataException">Throws if read bytes and is out of bounds of string size.</exception>
		public string ReadVirtualNTUnicodeString(ulong memoryLocation)
		{
			ushort stringSize = ReadVirtualMemory<ushort>(memoryLocation);
			ushort stringMaxSize = ReadVirtualMemory<ushort>(memoryLocation + 0x2);
			StringBuilder sb = new StringBuilder((int)stringMaxSize);

			CheckReturnHRAndThrow(_debugDataSpaces.ReadUnicodeStringVirtualWide(ReadVirtualPointer(memoryLocation + 0x8), stringMaxSize, sb, stringMaxSize, out uint bytesRead));
			if(bytesRead == 0 || bytesRead > stringMaxSize)
			{
				throw new InvalidDataException($"String Size is {stringSize} bytes with a maximum of {stringMaxSize} bytes but read {bytesRead} bytes.");
			}

			return sb.ToString();
		}

		/// <summary>
		/// Reads a sequences of raw bytes from memory and attempts to encode it as a unicode string.
		/// </summary>
		/// <param name="memoryLocation">starting location of memory to read</param>
		/// <param name="bytesToRead">number of bytes to read</param>
		/// <param name="isPointer">If true, will derefence the supplied memoryLocation before reading.
		///							If false will read memoryLocation memory directly.
		/// </param>
		/// <returns>Unicode encoded string.</returns>
		public string ReadVirtualUnicodeStringRaw(ulong memoryLocation, uint bytesToRead, bool isPointer = false)
		{
			if(isPointer)
			{
				memoryLocation = ReadVirtualPointer(memoryLocation);
			}
			
			return Encoding.Unicode.GetString(ReadVirtualMemory(memoryLocation, bytesToRead));
		}

		/// <summary>
		/// Attempts to read the commandline unicode string directly from the PEB.
		/// </summary>
		/// <returns>string representing the process command line</returns>
		private string GetProcessCommandLine()
		{
			CheckReturnHRAndThrow(_debugSystemObjects.GetCurrentProcessPeb(out ulong pebLocation));
			ulong processParams = ReadVirtualPointer(pebLocation + GetFieldOffsetFromSymbolByName("ntdll!_PEB", "ProcessParameters"));
			ulong commandLine = processParams + GetFieldOffsetFromSymbolByName("ntdll!_RTL_USER_PROCESS_PARAMETERS", "CommandLine");
			return ReadVirtualNTUnicodeString(commandLine);
		}

		/// <summary>
		/// Attempts to read the raw array of bytes for the process environment variables and format it as a unicode string.
		/// Each variable is terminated with a null character, the end of the unicode string is not double null terminated.
		/// This is read directly from the PEB.
		/// </summary>
		/// <returns>Environment variable block with internal null terminated strings.</returns>
		private string GetProcessEnvironmentStringRaw()
		{
			CheckReturnHRAndThrow(_debugSystemObjects.GetCurrentProcessPeb(out ulong pebLocation));
			ulong processParams = ReadVirtualPointer(pebLocation + GetFieldOffsetFromSymbolByName("ntdll!_PEB", "ProcessParameters"));
			ulong environmentSize = ReadVirtualMemory<ulong>(processParams + GetFieldOffsetFromSymbolByName("ntdll!_RTL_USER_PROCESS_PARAMETERS", "EnvironmentSize"));
			return ReadVirtualUnicodeStringRaw(processParams + GetFieldOffsetFromSymbolByName("ntdll!_RTL_USER_PROCESS_PARAMETERS", "Environment"), (uint)environmentSize, true);
		}

		/// <summary>
		/// Obtains the process environment variables directly from the PEB.
		/// </summary>
		/// <returns>Dictionary of environment variable key value pairs.</string></returns>
		private Dictionary<string, string> GetProcessEnvironmentVariables()
		{
			string[] rawVariables = GetProcessEnvironmentStringRaw().Split('\0');

			Dictionary<string, string> envDictionary = new Dictionary<string, string>();

			foreach(string s in rawVariables)
			{
				if(string.IsNullOrWhiteSpace(s))
				{
					continue;
				}

				string[] kvp = s.Split('=');
				if(kvp.Length != 2)
				{
					continue;
				}

				envDictionary.Add(kvp[0], kvp[1]);
			}

			return envDictionary;
		}

		/// <summary>
		/// Obtains the traceflags that were enabled on the instance of SQL.
		/// Attempts to find the end of the memory range, which may be incorrect
		/// if a full memory dump is used as all memory will exist and not just
		/// referenced or indirect memory.
		/// </summary>
		/// <returns>
		/// A list of traceflags that are enabled.
		/// </returns>
		private List<int> GetTraceFlags()
		{
			ulong start = GetSymbolLocationByName("sqlmin!g_rgUlTraceFlags");
			if (start == 0)
			{
				throw new InvalidDataException("Traceflag start location cannot be 0");
			}

			int counter = 0;
			List<int> flags = new List<int>();

			try
			{
				// I couldn't find anything in the public symbols to correspond to a maximum
				// for trace flags, so the best guess here is if we're 9k in, it's probably wrong
				// so just bail. Otherwise we should hit memory that doesn't exist, which tells us
				// that we're at the end of the list, since we'd need the memory for trace flags
				// referenced in a dump file if we wanted to be able to understand the state of
				// the environment and instance.
				// All of the above falls on its face if all of the memory is available.
				// It would need more investigation to see if there are various structures
				// we could use to find a known signature as the end.
				while (counter < 9000)
				{
					byte tfs = ReadVirtualMemory<byte>(start + (ulong)counter);
					counter++;

					if (tfs != 0)
					{
						for(int i = 0; i < 8; i++)
						{
							if((tfs & (1 << i)) > 0)
							{
								flags.Add((counter * 8) + i);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				// If the memory isn't available, don't throw
				if ((uint)ex.HResult != (uint)0x8007001E)
				{
					throw;
				}
			}

			return flags;
		}

		/// <summary>
		/// Given a debugger thread id (not a system thread id), symbolize the thread stack
		/// with the currently supplied symbol and image path.
		/// </summary>
		/// <param name="debuggerThreadID">debugger thread id, not the system thread id.</param>
		/// <returns>The thread call stack in a symbolized form.</returns>
		public string[] GetSymbolizedThreadStack(uint debuggerThreadID)
		{
			CheckReturnHRAndThrow(_debugSystemObjects.GetCurrentThreadId(out uint originalDebuggerThreadID));
			CheckReturnHRAndThrow(_debugSystemObjects.SetCurrentThreadId(debuggerThreadID));

			//generically 256 to save from another call for no reason
			DEBUG_STACK_FRAME_EX[] stackFramesEx = new DEBUG_STACK_FRAME_EX[256];
			CheckReturnHRAndThrow(_debugControl.GetStackTraceEx(0, 0, 0, stackFramesEx, stackFramesEx.Length, out uint framesFilled));

			string[] symbolizedStack = new string[framesFilled];
			for(int i = 0; i < framesFilled; i++)
			{
				StringBuilder sb = new StringBuilder(512);
				int HResult = _debugSymbols.GetNameByOffsetWide(stackFramesEx[i].InstructionOffset, sb, sb.Capacity, out uint nameSize, out ulong displacement);

				if (HResult != 0)
				{
					sb.Append(stackFramesEx[i].InstructionOffset.ToString());
				}

				symbolizedStack[i] = $"{sb.ToString()}+0x{displacement:X}";
			}

			CheckReturnHRAndThrow(_debugSystemObjects.SetCurrentThreadId(originalDebuggerThreadID));
			return symbolizedStack;
		}

		/// <summary>
		/// Given the command line of the process start, search for the instance name
		/// which will always be after -s .
		/// </summary>
		/// <param name="commandLine">Process creation command line.</param>
		/// <returns>SQL Server Instance Name</returns>
		private string GetSQLServerInstanceNameFromCommandLine(string commandLine)
		{
			int start = commandLine.IndexOf("-s") + 2;
			int end = commandLine.IndexOf(' ', start);
			if (end == -1)
			{
				end = commandLine.Length;
			}
			return commandLine.Substring(start, end - start);
		}

		/// <summary>
		/// Gets the manufacturer string of the system, for example:
		/// Microsoft Corporation
		/// Anazon, Inc.
		/// VMWare
		/// </summary>
		/// <returns>string, manufacturer name</returns>
		private string GetSystemManufacturer()
		{
			return ReadVirtualUnicodeStringRaw(GetSymbolLocationByName("sqlmin!HwInfo::sm_SystemManufacturer"), 128);
		}

		/// <summary>
		/// Gets the product name of the system, for example:
		/// Virtual Machine
		/// x1e.16xlarge
		/// </summary>
		/// <returns>string, product name</returns>
		private string GetSystemProductName()
		{
			return ReadVirtualUnicodeStringRaw(GetSymbolLocationByName("sqlmin!HwInfo::sm_SystemProductName"), 128);
		}

		/// <summary>
		/// Returns the value for the key given from the environment variables for the computer
		/// where the dump was taken. If not found, an empty string is returned.
		/// </summary>
		/// <param name="variableName">Name of the environment variable whose value you want.</param>
		/// <returns>Value of key if exists, otherwise empty string.</returns>
		private string TryGetValueFromEnvironmentVariables(string variableName)
		{
			if (EnvironmentVariables.TryGetValue(variableName, out var value))
			{
				return value;
			}

			return string.Empty;
		}

		/// <summary>
		/// Load thread data in the dump from various structures. Do not call until symbols and image locations are set.
		/// </summary>
		/// <returns>List of dumpthreadinfos</returns>
		private List<DumpThreadInfo> GetThreads()
		{
			List<DumpThreadInfo> threads = new List<DumpThreadInfo>(_reader.Threads.Count());
			List<MINIDUMP_THREAD> minidumpThreadData = _reader.Threads.ToList();

			for(uint i = 0; i < _reader.Threads.Count(); i++)
			{
				threads.Add(new DumpThreadInfo(i, minidumpThreadData[(int)i], GetSymbolizedThreadStack(i)));
			}

			return threads;
		}

		/// <summary>
		/// Loads module information from the dump.
		/// </summary>
		/// <returns>List of dumploadedmodulesinfos</returns>
		private List<DumpLoadedModuleInfo> GetLoadedModules()
		{
			List<DumpLoadedModuleInfo> loadedModules = new List<DumpLoadedModuleInfo>(_reader.LoadedModules.Count());
			foreach(KeyValuePair<string,MINIDUMP_MODULE> m in _reader.LoadedModules)
			{
				loadedModules.Add(new DumpLoadedModuleInfo(m.Value, m.Key));
			}

			return loadedModules;
		}

		/// <summary>
		/// Loads the unloaded module information from the dump.
		/// </summary>
		/// <returns>List of dumpunloadedmoduleinfos</returns>
		private List<DumpUnloadedModuleInfo> GetUnloadedModules()
		{
			List<DumpUnloadedModuleInfo> unloadedModules = new List<DumpUnloadedModuleInfo>(_reader.UnloadedModules.Count());
			foreach(KeyValuePair<string, MINIDUMP_UNLOADED_MODULE> u in _reader.UnloadedModules)
			{
				unloadedModules.Add(new DumpUnloadedModuleInfo(u.Value, u.Key));
			}

			return unloadedModules;
		}
	}
}
