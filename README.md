# SQLDbgEng

A very basic implementation of utilizing DbgEng with C# for the purpose of obtaining typical data from a SQL Server memory dump.
This is a monolithic implementation and was not made with extensibility or other items in mind. Merely it was made to obtain data
from the dump files in various ways, such as via output capture from the debugger, using debugger api calls, and via reading the dump
files directly and parsing them. No implementation has been done for debugger events.

Properties are lazy loaded as each item may or may not be used for different dumps.

SQL Server didn't always follow proper minidump rules, for example there should only be a single stream of each type. However, 
in the dumps for SQL Server there are typically between 1 and 3 streams for UserCommentW. There is no public extension for 
decompressing memory in dumps from SQL Server 2022 and above, however I did add a property for compressed memory segments.

There are no safety checks, if you don't open a dump successfully first, all of the calls will throw. You've been warned.

This comes with all of the baggage of DbgEng, for example, 1 open dump per process, syncronous processing, etc.

Official Symbols Server from MS has been included for ease of reference. Image path and Symbols path shsould be set accordingly.

This work is licensed under CC BY-NC-SA 4.0