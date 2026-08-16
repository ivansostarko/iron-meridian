namespace IronMeridian.Data
{
    /// <summary>
    /// The photographs an officer is shown by: five per side, under
    /// <c>Assets/Resources/Graphics/Commanders/</c>.
    ///
    /// **Why a face at all.** A roster of twenty officers is twenty rows of rank
    /// and surname, and a chain of command read that way is a spreadsheet. A
    /// portrait is the cheapest thing that turns a row into somebody — and once
    /// a face is attached to a name, "the division commander is out of action"
    /// is a thing that happened to a person rather than a flag that flipped.
    ///
    /// **Sides do not share a set.** The friendly and enemy photographs are two
    /// different armies' uniforms, so the side decides the folder and the index
    /// only chooses within it. A commander never crosses sides, so an index
    /// never has to be re-picked.
    ///
    /// **The index is stored, not re-rolled.** <see cref="CommanderState.portrait"/>
    /// is written once when the officer is created and travels in the map file,
    /// so the same face comes back on load. A roster saved before portraits
    /// existed has no index at all — those fall back to
    /// <see cref="StableIndex"/>, which derives one from the officer's id, so
    /// an old scenario gets a spread of faces that are nonetheless the *same*
    /// faces every time it is opened. A random pick at display time would give
    /// an officer a new face on every panel rebuild, which is the one thing a
    /// portrait must never do.
    ///
    /// See docs/23-COMMANDERS.md.
    /// </summary>
    public static class CommanderPortraits
    {
        /// <summary>Photographs available per side. Both folders carry the same number.</summary>
        public const int Count = 5;

        const string FriendlyPrefix = "Graphics/Commanders/Friendly/friendly_general_";
        const string EnemyPrefix = "Graphics/Commanders/Enemy/enemy_general_";

        /// <summary>A fresh portrait for a newly created officer.</summary>
        public static int Pick() => UnityEngine.Random.Range(0, Count);

        /// <summary>
        /// Which photograph this officer wears: his own if he has one, otherwise
        /// one derived from his id — see the class remarks.
        /// </summary>
        public static int IndexOf(CommanderState commander)
        {
            if (commander == null) return 0;
            return commander.portrait >= 0
                ? commander.portrait % Count
                : StableIndex(commander.id);
        }

        /// <summary>
        /// Resources path of an officer's photograph, ready for
        /// <c>UIFactory.LoadSprite</c>. Kept here rather than at the call sites
        /// so a screen that wants to show a face does not have to know how the
        /// folders are laid out.
        /// </summary>
        public static string PathFor(CommanderState commander) =>
            PathFor(commander == null ? Team.User : commander.TeamEnum, IndexOf(commander));

        public static string PathFor(Team team, int index)
        {
            // Wrapped rather than clamped, and 1-based on disk: an index out of
            // range is a catalogue that has shrunk, and the right answer to that
            // is a different face, not a missing one.
            int n = ((index % Count) + Count) % Count + 1;
            return (team == Team.Enemy ? EnemyPrefix : FriendlyPrefix) + n;
        }

        /// <summary>
        /// A face from an id — the same one every time, on every machine.
        /// <c>string.GetHashCode</c> is explicitly not guaranteed stable across
        /// runtimes, and a portrait that changed between a save and a load would
        /// be worse than no portrait at all, so the hash is spelled out here.
        /// </summary>
        static int StableIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            int hash = 17;
            foreach (char c in id) hash = unchecked(hash * 31 + c);
            return (hash & 0x7fffffff) % Count;
        }
    }
}
