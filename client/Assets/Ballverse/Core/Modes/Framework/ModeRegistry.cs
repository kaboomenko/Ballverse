using System;
using System.Collections.Generic;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Explicit registry of the mode definitions compiled into the build (spec §7.6 "Регистрация": no reflection, IL2CPP strips
    /// unused code). Only released catalog modes may be registered, each once; iteration is in catalog code order. The Remote
    /// Config kill switch <c>modes_disabled</c> (spec §7.6) filters <see cref="Available"/> without unregistering.
    /// Not thread-safe while registering; read-only use afterwards is safe.
    /// </summary>
    public sealed class ModeRegistry
    {
        readonly List<IModeDefinition> _modes = new List<IModeDefinition>();

        /// <summary>Registered definitions in <see cref="ModeCode"/> order.</summary>
        public IReadOnlyList<IModeDefinition> All => _modes;

        public int Count => _modes.Count;

        /// <summary>
        /// Registers a definition. Throws <see cref="ArgumentException"/> when its <see cref="IModeDefinition.Info"/> is not the
        /// catalog entry of a released mode (spec §2.8: codes and ids are stable; unreleased modes never appear) or its schema is
        /// missing, and <see cref="InvalidOperationException"/> when the code is already registered.
        /// </summary>
        public void Register(IModeDefinition mode)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            ModeInfo info = mode.Info ?? throw new ArgumentException("Mode has no catalog info", nameof(mode));
            ModeInfo catalog = ModeCatalog.Get(info.Code);
            if (!ReferenceEquals(catalog, info) && !string.Equals(catalog.Id, info.Id, StringComparison.Ordinal))
                throw new ArgumentException("Mode " + info.Code + " id '" + info.Id + "' differs from the catalog id '" + catalog.Id + "'", nameof(mode));
            if (!catalog.InRelease) throw new ArgumentException("Mode " + info.Code + " is not part of this release (spec §2.8)", nameof(mode));
            if (mode.Schema == null || mode.Schema.IsLayer) throw new ArgumentException("Mode " + info.Code + " needs a complete schema", nameof(mode));
            int insertAt = _modes.Count;
            for (int i = 0; i < _modes.Count; i++)
            {
                ModeCode c = _modes[i].Info.Code;
                if (c == info.Code) throw new InvalidOperationException("Mode " + info.Code + " is already registered");
                if (c > info.Code && insertAt == _modes.Count) insertAt = i;
            }
            _modes.Insert(insertAt, mode);
        }

        public bool TryGet(ModeCode code, out IModeDefinition mode)
        {
            foreach (IModeDefinition m in _modes)
            {
                if (m.Info.Code == code)
                {
                    mode = m;
                    return true;
                }
            }
            mode = null;
            return false;
        }

        /// <summary>By stable string id (e.g. "ring_escape", a replay header's modeId).</summary>
        public bool TryGet(string id, out IModeDefinition mode)
        {
            foreach (IModeDefinition m in _modes)
            {
                if (string.Equals(m.Info.Id, id, StringComparison.Ordinal))
                {
                    mode = m;
                    return true;
                }
            }
            mode = null;
            return false;
        }

        public IModeDefinition Get(ModeCode code)
        {
            if (!TryGet(code, out IModeDefinition m)) throw new KeyNotFoundException("Mode " + code + " is not registered");
            return m;
        }

        /// <summary>Registered modes not switched off by the Remote Config kill switch (<c>modes_disabled</c>), in code order.</summary>
        public IReadOnlyList<IModeDefinition> Available(IEnumerable<ModeCode> disabled)
        {
            var off = new HashSet<ModeCode>();
            if (disabled != null)
            {
                foreach (ModeCode c in disabled) off.Add(c);
            }
            var list = new List<IModeDefinition>(_modes.Count);
            foreach (IModeDefinition m in _modes)
            {
                if (!off.Contains(m.Info.Code)) list.Add(m);
            }
            return list;
        }
    }
}
