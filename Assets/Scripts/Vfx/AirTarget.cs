using System.Collections.Generic;
using UnityEngine;
using IronMeridian.Data;

namespace IronMeridian.Vfx
{
    /// <summary>
    /// Something in the air that ground-based air defence can find and shoot at.
    ///
    /// **Why a component and not a list of flights.** The three drone flights
    /// (<see cref="DroneRun"/>, <see cref="ReconDroneRun"/>) and anything added
    /// later are unrelated classes with unrelated trajectories, and the air
    /// picture must not care which is which — a radar sees a track, not a class
    /// name. So a flight attaches one of these to itself, keeps its geodetic
    /// position on it, and hands over what to do when it is hit. The air
    /// defence system then works entirely against <see cref="All"/> and never
    /// needs to know what is flying.
    ///
    /// **The position is pushed, not pulled.** The flights already compute
    /// latitude, longitude and altitude every frame to place their anchor;
    /// asking a <c>CesiumGlobeAnchor</c> for them back would be a round trip
    /// through ECEF for a number the caller was holding a moment earlier.
    ///
    /// See docs/24-AIR-DEFENCE.md.
    /// </summary>
    public class AirTarget : MonoBehaviour
    {
        static readonly List<AirTarget> _all = new List<AirTarget>();

        /// <summary>Every live air track. Compacted as flights end — see <see cref="OnDestroy"/>.</summary>
        public static IReadOnlyList<AirTarget> All => _all;

        /// <summary>Whose aircraft this is. Air defence engages the other side's.</summary>
        public Team Team { get; private set; }

        /// <summary>What to call it in a report — "Switchblade 600", "Recon UAS".</summary>
        public string Label { get; private set; }

        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        /// <summary>Height above the terrain under it, metres. What decides slant range.</summary>
        public double AltitudeMeters { get; private set; }

        /// <summary>
        /// True once a launcher has committed a missile to this track. A second
        /// battery must not fire at something already dead — that is how one
        /// drone attracts six missiles and every launcher on the map empties
        /// itself into the same piece of sky.
        /// </summary>
        public bool Engaged { get; private set; }

        /// <summary>True once the track has been killed; it stops being a target immediately.</summary>
        public bool Destroyed { get; private set; }

        /// <summary>
        /// What the flight does when it is hit. Set by the flight itself,
        /// because only the flight knows how to stop flying — cutting its
        /// engine note, cancelling its callbacks and handing its model to
        /// <see cref="DroneFall"/>.
        /// </summary>
        public System.Action ShotDown;

        /// <summary>Attaches a track to a flight and puts it on the air picture.</summary>
        public static AirTarget Attach(GameObject flight, Team team, string label)
        {
            var target = flight.AddComponent<AirTarget>();
            target.Team = team;
            target.Label = label;
            _all.Add(target);
            return target;
        }

        /// <summary>Called by the flight every time it moves.</summary>
        public void SetPosition(double lat, double lon, double altitudeMeters)
        {
            Latitude = lat;
            Longitude = lon;
            AltitudeMeters = altitudeMeters;
        }

        /// <summary>Marks the track as being shot at, so nothing else fires at it.</summary>
        public void MarkEngaged() => Engaged = true;

        /// <summary>
        /// Releases the commitment without killing the track — used when an
        /// engagement is abandoned because the launcher died or lost the
        /// contact, so another battery can take it on rather than the drone
        /// flying home untouched because of a launcher that no longer exists.
        /// </summary>
        public void ReleaseEngagement()
        {
            if (!Destroyed) Engaged = false;
        }

        /// <summary>The missile arrived. Once only, however many are in the air.</summary>
        public void Kill()
        {
            if (Destroyed) return;
            Destroyed = true;
            ShotDown?.Invoke();
        }

        void OnDestroy() => _all.Remove(this);
    }
}
