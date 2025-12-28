using Microsoft.Diagnostics.Runtime.Interop;
using System.Runtime.InteropServices;
using System.Text;

namespace SQLDbgEng.Logging
{
	public struct DebuggerOutput
	{
		public DEBUG_OUTPUT Mask;
		public string Text;
		public DebuggerOutput(DEBUG_OUTPUT Mask, string Text)
		{
			this.Mask = Mask;
			this.Text = Text;
		}
	}

	public class DebugOutputManager : IDebugOutputCallbacksWide
	{
		public event EventHandler<DebuggerOutput>? DebuggerOutputEvent;

		private readonly int S_OK = 0;
		public int Output([In] DEBUG_OUTPUT Mask, [In, MarshalAs(UnmanagedType.LPWStr)] string Text)
		{
			DebuggerOutputEvent?.Invoke(this, new DebuggerOutput(Mask, Text));
			return S_OK;
		}
	}

	public class DebuggerOutputSimpleCapture
	{
		StringBuilder m_debuggerOutput;

		public string CapturedDebuggerOutput
		{
			get { return m_debuggerOutput.ToString(); }
		}

		public DebuggerOutputSimpleCapture()
		{
			m_debuggerOutput = new StringBuilder();
		}

		public void Output(object? sender, DebuggerOutput e)
		{
			m_debuggerOutput.Append(e.Text);
		}
	}
}
