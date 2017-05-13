﻿using System.Collections.Generic;
using System.Linq;

using BizHawk.Emulation.Common;

namespace BizHawk.Emulation.Cores.Computers.Commodore64
{
	public partial class C64 : IDebuggable
	{
		private IDebuggable _selectedDebuggable;

		private IEnumerable<IDebuggable> GetAvailableDebuggables()
		{
			yield return _board.Cpu;
			if (_board.DiskDrive != null)
			{
				yield return _board.DiskDrive;
			}
		}

		private void SetDefaultDebuggable()
		{
			_selectedDebuggable = GetAvailableDebuggables().First();
		}

		public IDictionary<string, RegisterValue> GetCpuFlagsAndRegisters()
		{
			if (_selectedDebuggable == null)
			{
				SetDefaultDebuggable();
			}

			return _selectedDebuggable.GetCpuFlagsAndRegisters();
		}

		public void SetCpuRegister(string register, int value)
		{
			if (_selectedDebuggable == null)
			{
				SetDefaultDebuggable();
			}

			_selectedDebuggable.SetCpuRegister(register, value);
		}

		public bool CanStep(StepType type)
		{
			if (_selectedDebuggable == null)
			{
				SetDefaultDebuggable();
			}

			return _selectedDebuggable.CanStep(type);
		}


		public void Step(StepType type)
		{
			if (_selectedDebuggable == null)
			{
				SetDefaultDebuggable();
			}

			_selectedDebuggable.Step(type);
		}

		public int TotalExecutedCycles
		{
			get { return _selectedDebuggable.TotalExecutedCycles; }
		}

		private readonly IMemoryCallbackSystem _memoryCallbacks;

		IMemoryCallbackSystem IDebuggable.MemoryCallbacks => _memoryCallbacks;
	}
}
