using System;
using UnityEngine;

namespace Unite.Kernel
{
    /// <summary>
    /// Optional diagnostic output implemented by modules that expose a Vector2.
    /// </summary>
    public interface IVector2OutputSource
    {
        event Action<Vector2> Vector2OutputProduced;
    }
}

