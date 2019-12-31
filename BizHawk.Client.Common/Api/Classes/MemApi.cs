﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;

using BizHawk.Common.BufferExtensions;
using BizHawk.Emulation.Common;
using BizHawk.Emulation.Common.IEmulatorExtensions;

namespace BizHawk.Client.Common
{
	public sealed class MemApi : IMem
	{
		[RequiredService]
		private IEmulator Emulator { get; set; }

		[OptionalService]
		private IMemoryDomains MemoryDomainCore { get; set; }

		public MemApi(Action<string> logCallback)
		{
			LogCallback = logCallback;
		}

		public MemApi() : this(Console.WriteLine) {}

		private readonly Action<string> LogCallback;

		private bool _isBigEndian;

		private MemoryDomain _currentMemoryDomain;
		private MemoryDomain Domain
		{
			get
			{
				MemoryDomain LazyInit()
				{
					if (MemoryDomainCore == null)
					{
						var error = $"Error: {Emulator.Attributes().CoreName} does not implement memory domains";
						LogCallback(error);
						throw new NotImplementedException(error);
					}
					return MemoryDomainCore.HasSystemBus ? MemoryDomainCore.SystemBus : MemoryDomainCore.MainMemory;
				}
				_currentMemoryDomain ??= LazyInit();
				return _currentMemoryDomain;
			}
			set => _currentMemoryDomain = value;
		}

		private IMemoryDomains DomainList
		{
			get
			{
				if (MemoryDomainCore == null)
				{
					var error = $"Error: {Emulator.Attributes().CoreName} does not implement memory domains";
					LogCallback(error);
					throw new NotImplementedException(error);
				}
				return MemoryDomainCore;
			}
		}

		private MemoryDomain NamedDomainOrCurrent(string name)
		{
			if (!string.IsNullOrEmpty(name))
			{
				try
				{
					var found = DomainList[name];
					if (found != null) return found;
				}
				catch
				{
					// ignored
				}
				LogCallback($"Unable to find domain: {name}, falling back to current");
			}
			return Domain;
		}

		private uint ReadUnsignedByte(long addr, string domain = null)
		{
			var d = NamedDomainOrCurrent(domain);
			if (addr >= d.Size)
			{
				LogCallback($"Warning: attempted read of {addr} outside the memory size of {d.Size}");
				return default;
			}
			return d.PeekByte(addr);
		}

		private void WriteUnsignedByte(long addr, uint v, string domain = null)
		{
			var d = NamedDomainOrCurrent(domain);
			if (!d.CanPoke())
			{
				LogCallback($"Error: the domain {d.Name} is not writable");
				return;
			}
			if (addr >= d.Size)
			{
				LogCallback($"Warning: attempted write to {addr} outside the memory size of {d.Size}");
				return;
			}
			d.PokeByte(addr, (byte) v);
		}

		private static int U2S(uint u, int size)
		{
			var sh = 8 * (4 - size);
			return ((int) u << sh) >> sh;
		}

		#region Endian Handling

		private uint ReadUnsignedLittle(long addr, int size, string domain = null)
		{
			uint v = 0;
			for (var i = 0; i < size; i++) v |= ReadUnsignedByte(addr + i, domain) << (8 * i);
			return v;
		}

		private uint ReadUnsignedBig(long addr, int size, string domain = null)
		{
			uint v = 0;
			for (var i = 0; i < size; i++) v |= ReadUnsignedByte(addr + i, domain) << (8 * (size - 1 - i));
			return v;
		}

		private void WriteUnsignedLittle(long addr, uint v, int size, string domain = null)
		{
			for (var i = 0; i < size; i++) WriteUnsignedByte(addr + i, (v >> (8 * i)) & 0xFF, domain);
		}

		private void WriteUnsignedBig(long addr, uint v, int size, string domain = null)
		{
			for (var i = 0; i < size; i++) WriteUnsignedByte(addr + i, (v >> (8 * (size - 1 - i))) & 0xFF, domain);
		}

		private int ReadSigned(long addr, int size, string domain = null) => U2S(ReadUnsigned(addr, size, domain), size);

		private uint ReadUnsigned(long addr, int size, string domain = null) => _isBigEndian ? ReadUnsignedBig(addr, size, domain) : ReadUnsignedLittle(addr, size, domain);

		private void WriteSigned(long addr, int value, int size, string domain = null) => WriteUnsigned(addr, (uint) value, size, domain);

		private void WriteUnsigned(long addr, uint value, int size, string domain = null)
		{
			if (_isBigEndian) WriteUnsignedBig(addr, value, size, domain);
			else WriteUnsignedLittle(addr, value, size, domain);
		}

		#endregion

		#region Unique Library Methods

		public void SetBigEndian(bool enabled = true) => _isBigEndian = enabled;

		public List<string> GetMemoryDomainList()
		{
			var list = new List<string>();
			foreach (var domain in DomainList) list.Add(domain.Name);
			return list;
		}

		public uint GetMemoryDomainSize(string name = null) => (uint) NamedDomainOrCurrent(name).Size;

		public string GetCurrentMemoryDomain() => Domain.Name;

		public uint GetCurrentMemoryDomainSize() => (uint) Domain.Size;

		public bool UseMemoryDomain(string domain)
		{
			try
			{
				var found = DomainList[domain];
				if (found != null)
				{
					Domain = found;
					return true;
				}
			}
			catch
			{
				// ignored
			}
			LogCallback($"Unable to find domain: {domain}");
			return false;
		}

		public string HashRegion(long addr, int count, string domain = null)
		{
			var d = NamedDomainOrCurrent(domain);
			if (addr < 0 || addr >= d.Size)
			{
				var error = $"Address {addr} is outside the bounds of domain {d.Name}";
				LogCallback(error);
				throw new ArgumentOutOfRangeException(error);
			}
			if (addr + count > d.Size)
			{
				var error = $"Address {addr} + count {count} is outside the bounds of domain {d.Name}";
				LogCallback(error);
				throw new ArgumentOutOfRangeException(error);
			}
			var data = new byte[count];
			for (var i = 0; i < count; i++) data[i] = d.PeekByte(addr + i);
			using var hasher = SHA256.Create();
			return hasher.ComputeHash(data).BytesToHexString();
		}

		#endregion

		#region Common Special and Legacy Methods

		public uint ReadByte(long addr, string domain = null) => ReadUnsignedByte(addr, domain);

		public void WriteByte(long addr, uint value, string domain = null) => WriteUnsignedByte(addr, value, domain);

		public List<byte> ReadByteRange(long addr, int length, string domain = null)
		{
			var d = NamedDomainOrCurrent(domain);
			if (addr < 0) LogCallback($"Warning: Attempted reads on addresses {addr}..-1 outside range of domain {d.Name} in {nameof(ReadByteRange)}()");
			var lastReqAddr = addr + length - 1;
			var indexAfterLast = Math.Min(lastReqAddr, d.Size - 1) - addr + 1;
			var bytes = new byte[length];
			for (var i = addr < 0 ? -addr : 0; i != indexAfterLast; i++) bytes[i] = d.PeekByte(addr + i);
			if (lastReqAddr >= d.Size) LogCallback($"Warning: Attempted reads on addresses {d.Size}..{lastReqAddr} outside range of domain {d.Name} in {nameof(ReadByteRange)}()");
			return bytes.ToList();
		}

		public void WriteByteRange(long addr, List<byte> memoryblock, string domain = null)
		{
			var d = NamedDomainOrCurrent(domain);
			if (!d.CanPoke())
			{
				LogCallback($"Error: the domain {d.Name} is not writable");
				return;
			}
			if (addr < 0) LogCallback($"Warning: Attempted reads on addresses {addr}..-1 outside range of domain {d.Name} in {nameof(WriteByteRange)}()");
			var lastReqAddr = addr + memoryblock.Count - 1;
			var indexAfterLast = Math.Min(lastReqAddr, d.Size - 1) - addr + 1;
			for (var i = addr < 0 ? (int) -addr : 0; i != indexAfterLast; i++) d.PokeByte(addr + i, memoryblock[i]);
			if (lastReqAddr >= d.Size) LogCallback($"Warning: Attempted reads on addresses {d.Size}..{lastReqAddr} outside range of domain {d.Name} in {nameof(WriteByteRange)}()");
		}

		public float ReadFloat(long addr, string domain = null)
		{
			var d = NamedDomainOrCurrent(domain);
			if (addr >= d.Size)
			{
				LogCallback($"Warning: Attempted read {addr} outside memory size of {d.Size}");
				return default;
			}
			return BitConverter.ToSingle(BitConverter.GetBytes(d.PeekUint(addr, _isBigEndian)), 0);
		}

		public void WriteFloat(long addr, double value, string domain = null)
		{
			var d = NamedDomainOrCurrent(domain);
			if (!d.CanPoke())
			{
				LogCallback($"Error: the domain {Domain.Name} is not writable");
				return;
			}
			if (addr >= d.Size)
			{
				LogCallback($"Warning: Attempted write {addr} outside memory size of {d.Size}");
				return;
			}
			d.PokeUint(addr, BitConverter.ToUInt32(BitConverter.GetBytes((float) value), 0), _isBigEndian);
		}

		#endregion

		#region 1 Byte

		public int ReadS8(long addr, string domain = null) => (sbyte) ReadUnsignedByte(addr, domain);

		public uint ReadU8(long addr, string domain = null) => (byte) ReadUnsignedByte(addr, domain);

		public void WriteS8(long addr, int value, string domain = null) => WriteSigned(addr, value, 1, domain);

		public void WriteU8(long addr, uint value, string domain = null) => WriteUnsignedByte(addr, value, domain);

		#endregion

		#region 2 Byte

		public int ReadS16(long addr, string domain = null) => (short) ReadSigned(addr, 2, domain);

		public uint ReadU16(long addr, string domain = null) => (ushort) ReadUnsigned(addr, 2, domain);

		public void WriteS16(long addr, int value, string domain = null) => WriteSigned(addr, value, 2, domain);

		public void WriteU16(long addr, uint value, string domain = null) => WriteUnsigned(addr, value, 2, domain);

		#endregion

		#region 3 Byte

		public int ReadS24(long addr, string domain = null) => ReadSigned(addr, 3, domain);

		public uint ReadU24(long addr, string domain = null) => ReadUnsigned(addr, 3, domain);

		public void WriteS24(long addr, int value, string domain = null) => WriteSigned(addr, value, 3, domain);

		public void WriteU24(long addr, uint value, string domain = null) => WriteUnsigned(addr, value, 3, domain);

		#endregion

		#region 4 Byte

		public int ReadS32(long addr, string domain = null) => ReadSigned(addr, 4, domain);

		public uint ReadU32(long addr, string domain = null) => ReadUnsigned(addr, 4, domain);

		public void WriteS32(long addr, int value, string domain = null) => WriteSigned(addr, value, 4, domain);

		public void WriteU32(long addr, uint value, string domain = null) => WriteUnsigned(addr, value, 4, domain);

		#endregion
	}
}