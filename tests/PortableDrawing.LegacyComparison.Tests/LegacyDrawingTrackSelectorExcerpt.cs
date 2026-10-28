using System.Collections.Generic;
using Kinovea.Services;

namespace Kinovea.ScreenManager.LegacySourceExcerpt;

// This method body is copied exactly from DrawingTrack.FindClosestPoint(long, List<TimedPoint>).
// Run tools/verify-drawing-track-selector-excerpt.py before the comparison suite to
// prove the executable excerpt still matches the checked-out production source.
internal static class LegacyDrawingTrackSelector
{
    public static int FindClosestPoint(long currentTimestamp, List<TimedPoint> positions)
    {
            // Find the closest registered timestamp
            // Parameter is given in absolute timestamp.
            long minErr = long.MaxValue;
            int closest = 0;

            for (int i = 0; i < positions.Count; i++)
            {
                long err = Math.Abs(positions[i].T - currentTimestamp);
                if (err < minErr)
                {
                    minErr = err;
                    closest = i;
                }
            }

            return closest;
    }
}
