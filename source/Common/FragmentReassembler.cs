using System;
using System.Collections.Generic;

namespace GameNet.Common
{
    public sealed class FragmentReassembler
    {
        private sealed class State
        {
            public byte[] Buffer;
            public long Total;
            public long Received;
        }

        private readonly Dictionary<Guid, State> _states = new Dictionary<Guid, State>();

        public bool Add(Guid id, long totalLen, long offset, ArraySegment<byte> chunk, out byte[] completed)
        {
            if (totalLen <= 0 || totalLen > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(totalLen));
            if (!_states.TryGetValue(id, out var st))
            {
                st = new State { Buffer = new byte[totalLen], Total = totalLen, Received = 0 };
                _states[id] = st;
            }

            Buffer.BlockCopy(chunk.Array, chunk.Offset, st.Buffer, checked((int)offset), chunk.Count);
            st.Received += chunk.Count;

            if (st.Received >= st.Total)
            {
                completed = st.Buffer;
                _states.Remove(id);
                return true;
            }

            completed = null;
            return false;
        }

        public void Abort(Guid id) => _states.Remove(id);
        public void Clear() => _states.Clear();
    }
}
