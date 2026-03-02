using UnityEngine;

namespace Tiogiras.PVTM
{
    
/// <summary> Utility class storing utility methods </summary>
public static class Utility
{
    /// <summary> Copy and return a vector with the overwrite x value </summary>
    /// <param name="original"> The vector to copy </param>
    /// <param name="x"> The value to overwrite the original x value with </param>
    public static Vector3 CopyWithX(this Vector3 original, float x)
    {
        return new Vector3(x, original.y, original.z);
    }

    /// <summary> Copy and return a vector with the overwrite y value </summary>
    /// <param name="original"> The vector to copy </param>
    /// <param name="y"> The value to overwrite the original y value with </param>
    public static Vector3 CopyWithY(this Vector3 original, float y)
    {
        return new Vector3(original.x, y, original.z);
    }
}

}