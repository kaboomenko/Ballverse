using System;
using System.IO;

namespace Ballverse.Core.RenderStream.Internal
{
    /// <summary>
    /// Encodes / decodes the frames of one block (spec §7.4 "между опорными кадрами — дельты от линейного прогноза").
    /// The same instance type is used by the writer and the reader, so both sides keep identical prediction state.
    /// <para>Raw (uncompressed) block layout — frame 0 is a keyframe, frames 1..n-1 are deltas:</para>
    /// <code>
    /// keyframe: uvarint bodyCount · body[bodyCount] · uvarint hudCount · hud[hudCount]
    ///   body:   id · kind u8 · x svarint · y svarint · angle uvarint · scale uvarint · color u8 · flags u8
    ///   hud:    key · value svarint
    /// delta:    uvarint despawnCount · id[despawnCount]
    ///           uvarint spawnCount · body[spawnCount]
    ///           survivor[*]            (one per body of the previous frame that was not despawned, ascending id)
    ///           uvarint hudRemovedCount · key[hudRemovedCount]
    ///           uvarint hudSetCount · (key · value svarint)[hudSetCount]
    ///   survivor: mask u8 (bit0 x, bit1 y, bit2 angle, bit3 scale, bit4 color, bit5 flags; bits 6-7 zero)
    ///             then, for each set bit in that order: residual svarint (x, y, angle, scale) or u8 (color, flags)
    /// ids / keys in a list: first as svarint (ids) or uvarint (keys), then uvarint (gap - 1) — lists are strictly ascending.
    /// </code>
    /// <para>
    /// Residuals: each 16-bit channel (x, y as int16 bits, angle, scale) is predicted as 2·prev − prevprev when the body
    /// existed in the two previous frames of this block, otherwise as prev; residual = (cur − prediction) wrapped to int16.
    /// Wrapping keeps teleports small and the decode bit-exact. HUD set values are deltas from the previous value for
    /// existing keys and absolute for new keys. A body whose kind changes is coded as despawn + spawn.
    /// </para>
    /// </summary>
    internal sealed class FrameCodec
    {
        /// <summary>Hard cap on bodies per frame accepted by the decoder (allocation guard).</summary>
        public const int MaxBodiesPerFrame = 1 << 20;
        /// <summary>Hard cap on HUD entries per frame (<see cref="HudEntry.MaxEntriesPerFrame"/>).</summary>
        public const int MaxHudEntries = HudEntry.MaxEntriesPerFrame;

        const int MaskX = 1;
        const int MaskY = 2;
        const int MaskAngle = 4;
        const int MaskScale = 8;
        const int MaskColor = 16;
        const int MaskFlags = 32;
        const int MaskInvalid = 0xC0;

        struct Track
        {
            public BodyState Body;
            public ushort PX, PY, PA, PS;
            public bool HasPrev2;
        }

        Track[] _cur = new Track[16];
        int _curCount;
        Track[] _next = new Track[16];
        HudEntry[] _hud = new HudEntry[4];
        int _hudCount;
        HudEntry[] _hudNext = new HudEntry[4];
        int _framesInBlock;

        // Scratch, reused.
        int[] _intScratch = new int[16];
        int[] _intScratch2 = new int[16];
        bool[] _dead = new bool[16];
        BodyState[] _spawns = new BodyState[16];
        HudEntry[] _hudSet = new HudEntry[4];

        /// <summary>Number of frames coded since the last <see cref="ResetBlock"/>.</summary>
        public int FramesInBlock => _framesInBlock;

        /// <summary>Starts a new block: the next frame is a keyframe.</summary>
        public void ResetBlock()
        {
            _framesInBlock = 0;
            _curCount = 0;
            _hudCount = 0;
        }

        // ------------------------------------------------------------------ encode

        /// <summary>Encodes a validated frame (bodies sorted by unique id, HUD sorted by unique key).</summary>
        public void Encode(RenderFrame frame, ByteWriter w)
        {
            if (_framesInBlock == 0) EncodeKeyframe(frame, w);
            else EncodeDelta(frame, w);
            _framesInBlock++;
        }

        void EncodeKeyframe(RenderFrame frame, ByteWriter w)
        {
            var bodies = frame.Bodies;
            w.WriteUVarint((ulong)bodies.Length);
            EnsureTracks(ref _next, bodies.Length);
            for (int i = 0; i < bodies.Length; i++)
            {
                WriteBody(w, bodies[i], i == 0, i == 0 ? 0 : bodies[i - 1].Id);
                _next[i] = new Track { Body = bodies[i] };
            }
            SwapTracks(bodies.Length);

            var hud = frame.Hud;
            w.WriteUVarint((ulong)hud.Length);
            EnsureHud(ref _hudNext, hud.Length);
            for (int i = 0; i < hud.Length; i++)
            {
                WriteKey(w, hud[i].Key, i == 0, i == 0 ? 0 : hud[i - 1].Key);
                w.WriteSVarint(hud[i].Value);
                _hudNext[i] = hud[i];
            }
            SwapHud(hud.Length);
        }

        void EncodeDelta(RenderFrame frame, ByteWriter w)
        {
            var bodies = frame.Bodies;
            int n = bodies.Length;
            EnsureInts(ref _intScratch, _curCount);       // despawned prev indices
            EnsureInts(ref _intScratch2, n);              // for each new body: prev index or -1
            int despawnCount = 0;

            int i = 0, j = 0;
            while (i < _curCount || j < n)
            {
                if (j >= n || (i < _curCount && _cur[i].Body.Id < bodies[j].Id))
                {
                    _intScratch[despawnCount++] = i++;
                }
                else if (i >= _curCount || bodies[j].Id < _cur[i].Body.Id)
                {
                    _intScratch2[j++] = -1;
                }
                else
                {
                    if (_cur[i].Body.Kind != bodies[j].Kind)
                    {
                        _intScratch[despawnCount++] = i;
                        _intScratch2[j] = -1;
                    }
                    else
                    {
                        _intScratch2[j] = i;
                    }
                    i++;
                    j++;
                }
            }

            // Despawns.
            w.WriteUVarint((ulong)despawnCount);
            for (int k = 0; k < despawnCount; k++)
            {
                int id = _cur[_intScratch[k]].Body.Id;
                WriteId(w, id, k == 0, k == 0 ? 0 : _cur[_intScratch[k - 1]].Body.Id);
            }

            // Spawns.
            int spawnCount = 0;
            for (int k = 0; k < n; k++)
            {
                if (_intScratch2[k] < 0) spawnCount++;
            }
            w.WriteUVarint((ulong)spawnCount);
            int lastSpawnId = 0;
            bool firstSpawn = true;
            for (int k = 0; k < n; k++)
            {
                if (_intScratch2[k] >= 0) continue;
                WriteBody(w, bodies[k], firstSpawn, lastSpawnId);
                lastSpawnId = bodies[k].Id;
                firstSpawn = false;
            }

            // Survivors (ascending id = order of the new frame) and next prediction state.
            EnsureTracks(ref _next, n);
            for (int k = 0; k < n; k++)
            {
                int p = _intScratch2[k];
                BodyState b = bodies[k];
                if (p < 0)
                {
                    _next[k] = new Track { Body = b };
                    continue;
                }

                ref Track t = ref _cur[p];
                int rx = Residual((ushort)b.X, (ushort)t.Body.X, t.PX, t.HasPrev2);
                int ry = Residual((ushort)b.Y, (ushort)t.Body.Y, t.PY, t.HasPrev2);
                int ra = Residual(b.Angle, t.Body.Angle, t.PA, t.HasPrev2);
                int rs = Residual(b.Scale, t.Body.Scale, t.PS, t.HasPrev2);
                int mask = 0;
                if (rx != 0) mask |= MaskX;
                if (ry != 0) mask |= MaskY;
                if (ra != 0) mask |= MaskAngle;
                if (rs != 0) mask |= MaskScale;
                if (b.Color != t.Body.Color) mask |= MaskColor;
                if (b.Flags != t.Body.Flags) mask |= MaskFlags;
                w.WriteByte((byte)mask);
                if (rx != 0) w.WriteSVarint(rx);
                if (ry != 0) w.WriteSVarint(ry);
                if (ra != 0) w.WriteSVarint(ra);
                if (rs != 0) w.WriteSVarint(rs);
                if ((mask & MaskColor) != 0) w.WriteByte(b.Color);
                if ((mask & MaskFlags) != 0) w.WriteByte((byte)b.Flags);

                _next[k] = new Track
                {
                    Body = b,
                    PX = (ushort)t.Body.X,
                    PY = (ushort)t.Body.Y,
                    PA = t.Body.Angle,
                    PS = t.Body.Scale,
                    HasPrev2 = true,
                };
            }
            SwapTracks(n);

            EncodeHudDelta(frame.Hud, w);
        }

        void EncodeHudDelta(ReadOnlySpan<HudEntry> hud, ByteWriter w)
        {
            // Removed keys.
            int removed = 0;
            int i = 0, j = 0;
            while (i < _hudCount)
            {
                if (j >= hud.Length || _hud[i].Key < hud[j].Key) { removed++; i++; }
                else if (hud[j].Key < _hud[i].Key) j++;
                else { i++; j++; }
            }
            w.WriteUVarint((ulong)removed);
            int lastKey = 0;
            bool first = true;
            i = 0;
            j = 0;
            while (i < _hudCount)
            {
                if (j >= hud.Length || _hud[i].Key < hud[j].Key)
                {
                    WriteKey(w, _hud[i].Key, first, lastKey);
                    lastKey = _hud[i].Key;
                    first = false;
                    i++;
                }
                else if (hud[j].Key < _hud[i].Key) j++;
                else { i++; j++; }
            }

            // Set (new or changed) keys.
            int setCount = 0;
            i = 0;
            for (int k = 0; k < hud.Length; k++)
            {
                while (i < _hudCount && _hud[i].Key < hud[k].Key) i++;
                bool existed = i < _hudCount && _hud[i].Key == hud[k].Key;
                if (!existed || _hud[i].Value != hud[k].Value) setCount++;
            }
            w.WriteUVarint((ulong)setCount);
            first = true;
            lastKey = 0;
            i = 0;
            for (int k = 0; k < hud.Length; k++)
            {
                while (i < _hudCount && _hud[i].Key < hud[k].Key) i++;
                bool existed = i < _hudCount && _hud[i].Key == hud[k].Key;
                if (existed && _hud[i].Value == hud[k].Value) continue;
                WriteKey(w, hud[k].Key, first, lastKey);
                lastKey = hud[k].Key;
                first = false;
                long value = existed ? unchecked(hud[k].Value - _hud[i].Value) : hud[k].Value;
                w.WriteSVarint(value);
            }

            EnsureHud(ref _hudNext, hud.Length);
            hud.CopyTo(_hudNext);
            SwapHud(hud.Length);
        }

        static int Residual(ushort cur, ushort prev, ushort prev2, bool hasPrev2)
        {
            int prediction = hasPrev2 ? 2 * prev - prev2 : prev;
            return unchecked((short)(cur - prediction));
        }

        static void WriteBody(ByteWriter w, BodyState b, bool first, int previousId)
        {
            WriteId(w, b.Id, first, previousId);
            w.WriteByte((byte)b.Kind);
            w.WriteSVarint(b.X);
            w.WriteSVarint(b.Y);
            w.WriteUVarint(b.Angle);
            w.WriteUVarint(b.Scale);
            w.WriteByte(b.Color);
            w.WriteByte((byte)b.Flags);
        }

        static void WriteId(ByteWriter w, int id, bool first, int previousId)
        {
            if (first) w.WriteSVarint(id);
            else w.WriteUVarint((ulong)((long)id - previousId - 1));
        }

        static void WriteKey(ByteWriter w, int key, bool first, int previousKey)
        {
            if (first) w.WriteUVarint((ulong)key);
            else w.WriteUVarint((ulong)(key - previousKey - 1));
        }

        // ------------------------------------------------------------------ decode

        /// <summary>
        /// Decodes the next frame of the block into <paramref name="dest"/> (sorted by id / key).
        /// Throws <see cref="InvalidDataException"/> on any malformed data.
        /// </summary>
        public void Decode(ref ByteReader r, RenderFrame dest)
        {
            Skip(ref r);
            CopyCurrent(dest);
        }

        /// <summary>Copies the most recently coded frame into <paramref name="dest"/>.</summary>
        public void CopyCurrent(RenderFrame dest)
        {
            dest.EnsureBodyCapacity(_curCount);
            dest.EnsureHudCapacity(_hudCount);
            var bodies = dest.BodyArray;
            for (int i = 0; i < _curCount; i++) bodies[i] = _cur[i].Body;
            Array.Copy(_hud, dest.HudArray, _hudCount);
            dest.SetCounts(_curCount, _hudCount);
        }

        /// <summary>Decodes the next frame without materializing it (used when seeking within a block).</summary>
        public void Skip(ref ByteReader r)
        {
            if (_framesInBlock == 0) DecodeKeyframe(ref r);
            else DecodeDelta(ref r);
            _framesInBlock++;
        }

        void DecodeKeyframe(ref ByteReader r)
        {
            int count = r.ReadCount(Math.Min(MaxBodiesPerFrame, r.Remaining), "body count");
            EnsureTracks(ref _next, count);
            int previousId = 0;
            for (int i = 0; i < count; i++)
            {
                BodyState b = ReadBody(ref r, i == 0, previousId);
                previousId = b.Id;
                _next[i] = new Track { Body = b };
            }
            SwapTracks(count);

            int hudCount = r.ReadCount(Math.Min(MaxHudEntries, r.Remaining), "HUD count");
            EnsureHud(ref _hudNext, hudCount);
            int previousKey = 0;
            for (int i = 0; i < hudCount; i++)
            {
                int key = ReadKey(ref r, i == 0, previousKey);
                previousKey = key;
                _hudNext[i] = new HudEntry(key, ReadInt(ref r));
            }
            SwapHud(hudCount);
        }

        void DecodeDelta(ref ByteReader r)
        {
            // Despawns: ids must exist in the previous frame.
            int despawnCount = r.ReadCount(_curCount, "despawn count");
            EnsureBools(ref _dead, _curCount);
            Array.Clear(_dead, 0, _curCount);
            int scan = 0;
            int previousId = 0;
            for (int k = 0; k < despawnCount; k++)
            {
                int id = ReadId(ref r, k == 0, previousId);
                previousId = id;
                while (scan < _curCount && _cur[scan].Body.Id < id) scan++;
                if (scan >= _curCount || _cur[scan].Body.Id != id) throw new InvalidDataException("Despawn of unknown body id " + id);
                _dead[scan++] = true;
            }

            // Spawns.
            int spawnCount = r.ReadCount(Math.Min(MaxBodiesPerFrame, r.Remaining), "spawn count");
            EnsureBodies(ref _spawns, spawnCount);
            previousId = 0;
            for (int k = 0; k < spawnCount; k++)
            {
                _spawns[k] = ReadBody(ref r, k == 0, previousId);
                previousId = _spawns[k].Id;
            }

            int survivors = _curCount - despawnCount;
            int total = survivors + spawnCount;
            if (total > MaxBodiesPerFrame) throw new InvalidDataException("Too many bodies in frame");
            EnsureTracks(ref _next, total);

            // Merge survivors (reading their records in ascending id order) with spawns.
            int i = 0, s = 0, o = 0;
            while (true)
            {
                while (i < _curCount && _dead[i]) i++;
                bool haveSurvivor = i < _curCount;
                bool haveSpawn = s < spawnCount;
                if (!haveSurvivor && !haveSpawn) break;
                if (haveSurvivor && haveSpawn && _cur[i].Body.Id == _spawns[s].Id)
                {
                    throw new InvalidDataException("Spawned id " + _spawns[s].Id + " already alive");
                }
                if (haveSpawn && (!haveSurvivor || _spawns[s].Id < _cur[i].Body.Id))
                {
                    _next[o++] = new Track { Body = _spawns[s++] };
                    continue;
                }

                ref Track t = ref _cur[i];
                int mask = r.ReadByte();
                if ((mask & MaskInvalid) != 0) throw new InvalidDataException("Invalid survivor mask");
                BodyState b = t.Body;
                b.X = (short)Apply((ushort)t.Body.X, t.PX, t.HasPrev2, (mask & MaskX) != 0 ? ReadResidual(ref r) : 0);
                b.Y = (short)Apply((ushort)t.Body.Y, t.PY, t.HasPrev2, (mask & MaskY) != 0 ? ReadResidual(ref r) : 0);
                b.Angle = Apply(t.Body.Angle, t.PA, t.HasPrev2, (mask & MaskAngle) != 0 ? ReadResidual(ref r) : 0);
                b.Scale = Apply(t.Body.Scale, t.PS, t.HasPrev2, (mask & MaskScale) != 0 ? ReadResidual(ref r) : 0);
                if ((mask & MaskColor) != 0) b.Color = r.ReadByte();
                if ((mask & MaskFlags) != 0) b.Flags = (BodyFlags)r.ReadByte();
                _next[o++] = new Track
                {
                    Body = b,
                    PX = (ushort)t.Body.X,
                    PY = (ushort)t.Body.Y,
                    PA = t.Body.Angle,
                    PS = t.Body.Scale,
                    HasPrev2 = true,
                };
                i++;
            }
            SwapTracks(o);

            DecodeHudDelta(ref r);
        }

        void DecodeHudDelta(ref ByteReader r)
        {
            int removedCount = r.ReadCount(_hudCount, "HUD removed count");
            EnsureBools(ref _dead, _hudCount);
            Array.Clear(_dead, 0, _hudCount);
            int scan = 0;
            int previousKey = 0;
            for (int k = 0; k < removedCount; k++)
            {
                int key = ReadKey(ref r, k == 0, previousKey);
                previousKey = key;
                while (scan < _hudCount && _hud[scan].Key < key) scan++;
                if (scan >= _hudCount || _hud[scan].Key != key) throw new InvalidDataException("Removal of unknown HUD key " + key);
                _dead[scan++] = true;
            }

            int setCount = r.ReadCount(Math.Min(MaxHudEntries, r.Remaining), "HUD set count");
            EnsureHud(ref _hudSet, setCount);
            previousKey = 0;
            for (int k = 0; k < setCount; k++)
            {
                int key = ReadKey(ref r, k == 0, previousKey);
                previousKey = key;
                _hudSet[k] = new HudEntry(key, ReadInt(ref r));
            }

            // Unchanged HUD (the common case): keep the current state, no merge.
            if (removedCount == 0 && setCount == 0) return;

            EnsureHud(ref _hudNext, Math.Min(MaxHudEntries, _hudCount - removedCount + setCount));
            int i = 0, s = 0, o = 0;
            while (true)
            {
                while (i < _hudCount && _dead[i]) i++;
                bool haveOld = i < _hudCount;
                bool haveSet = s < setCount;
                if (!haveOld && !haveSet) break;
                if (o == MaxHudEntries) throw new InvalidDataException("Too many HUD entries in frame");
                if (haveOld && (!haveSet || _hud[i].Key < _hudSet[s].Key))
                {
                    _hudNext[o++] = _hud[i++];
                }
                else if (!haveOld || _hudSet[s].Key < _hud[i].Key)
                {
                    _hudNext[o++] = _hudSet[s++];
                }
                else
                {
                    _hudNext[o++] = new HudEntry(_hud[i].Key, unchecked(_hud[i].Value + _hudSet[s].Value));
                    i++;
                    s++;
                }
            }
            SwapHud(o);
        }

        static ushort Apply(ushort prev, ushort prev2, bool hasPrev2, int residual)
        {
            int prediction = hasPrev2 ? 2 * prev - prev2 : prev;
            return unchecked((ushort)(prediction + residual));
        }

        static int ReadResidual(ref ByteReader r)
        {
            long v = r.ReadSVarint();
            if (v < short.MinValue || v > short.MaxValue) throw new InvalidDataException("Residual out of int16 range");
            return (int)v;
        }

        static int ReadInt(ref ByteReader r)
        {
            long v = r.ReadSVarint();
            if (v < int.MinValue || v > int.MaxValue) throw new InvalidDataException("Value out of int32 range");
            return (int)v;
        }

        static BodyState ReadBody(ref ByteReader r, bool first, int previousId)
        {
            int id = ReadId(ref r, first, previousId);
            byte kind = r.ReadByte();
            if (kind > (byte)BodyKind.Other) throw new InvalidDataException("Unknown body kind " + kind);
            long x = r.ReadSVarint();
            long y = r.ReadSVarint();
            if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue)
            {
                throw new InvalidDataException("Position out of int16 range");
            }
            ulong angle = r.ReadUVarint();
            ulong scale = r.ReadUVarint();
            if (angle > ushort.MaxValue || scale > ushort.MaxValue) throw new InvalidDataException("Angle/scale out of uint16 range");
            byte color = r.ReadByte();
            byte flags = r.ReadByte();
            return new BodyState(id, (BodyKind)kind, (short)x, (short)y, (ushort)angle, (ushort)scale, color, (BodyFlags)flags);
        }

        static int ReadId(ref ByteReader r, bool first, int previousId)
        {
            if (first)
            {
                long v = r.ReadSVarint();
                if (v < int.MinValue || v > int.MaxValue) throw new InvalidDataException("Body id out of int32 range");
                return (int)v;
            }
            ulong gap = r.ReadUVarint();
            if (gap > (ulong)((long)int.MaxValue - previousId - 1) || (long)previousId == int.MaxValue)
            {
                throw new InvalidDataException("Body id out of int32 range");
            }
            return (int)(previousId + 1 + (long)gap);
        }

        static int ReadKey(ref ByteReader r, bool first, int previousKey)
        {
            ulong v = r.ReadUVarint();
            ulong key = first ? v : (ulong)previousKey + 1 + v;
            if (v > HudEntry.MaxKey || key > HudEntry.MaxKey) throw new InvalidDataException("HUD key out of range");
            return (int)key;
        }

        // ------------------------------------------------------------------ buffers

        void SwapTracks(int count)
        {
            var t = _cur;
            _cur = _next;
            _next = t;
            _curCount = count;
        }

        void SwapHud(int count)
        {
            var t = _hud;
            _hud = _hudNext;
            _hudNext = t;
            _hudCount = count;
        }

        static void EnsureTracks(ref Track[] a, int n)
        {
            if (a.Length < n) a = new Track[Math.Max(n, a.Length * 2)];
        }

        static void EnsureHud(ref HudEntry[] a, int n)
        {
            if (a.Length < n) a = new HudEntry[Math.Max(n, a.Length * 2)];
        }

        static void EnsureBodies(ref BodyState[] a, int n)
        {
            if (a.Length < n) a = new BodyState[Math.Max(n, a.Length * 2)];
        }

        static void EnsureInts(ref int[] a, int n)
        {
            if (a.Length < n) a = new int[Math.Max(n, a.Length * 2)];
        }

        static void EnsureBools(ref bool[] a, int n)
        {
            if (a.Length < n) a = new bool[Math.Max(n, a.Length * 2)];
        }
    }
}
