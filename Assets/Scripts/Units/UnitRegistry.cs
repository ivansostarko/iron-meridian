using System.Collections.Generic;
using IronMeridian.Data;

namespace IronMeridian.Units
{
    /// <summary>Runtime registry of all units on the map.</summary>
    public static class UnitRegistry
    {
        public static readonly List<UnitActor> All = new List<UnitActor>();

        public static event System.Action Changed;

        public static void Register(UnitActor a) { All.Add(a); Changed?.Invoke(); }
        public static void Unregister(UnitActor a) { All.Remove(a); Changed?.Invoke(); }
        public static void Clear() { All.Clear(); Changed?.Invoke(); }

        public static IEnumerable<UnitActor> OfTeam(Team team)
        {
            foreach (var a in All)
                if (a != null && a.State.TeamEnum == team && a.IsAlive) yield return a;
        }

        public static void NotifyMoved() => Changed?.Invoke();
    }
}
