using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using IronMeridian.Core;
using IronMeridian.Data;
using IronMeridian.Map;
using IronMeridian.Units;

namespace IronMeridian.UI
{
    /// <summary>
    /// Folds crowded formations into a single counted marker once the camera is
    /// too far out to draw them individually. **Battle mode only.**
    ///
    /// An operational map pulled back to corps scale puts a hundred APP-6
    /// frames into a few hundred pixels: the icons overlap into a smear, every
    /// one of them is still a click target on top of every other one, and the
    /// picture stops saying anything about where the mass actually is. A marker
    /// reading "12" over the right patch of ground says more than twelve
    /// unreadable counters do.
    ///
    /// It is deliberately **not** on in the scenario editor. Laying an order of
    /// battle out means placing and dragging individual counters, and a system
    /// that removes the thing you are trying to grab the moment you zoom out to
    /// see the whole map would make the editor unusable. Zoom in, or stop the
    /// battle, and every unit comes straight back.
    ///
    /// Clustering is done in **screen space**, not on the globe: what is being
    /// fixed is icons overlapping each other on screen, which is a screen-space
    /// problem — two formations 5 km apart need a marker at one zoom and not at
    /// another. The grouping pass is greedy over the registry in its own order,
    /// which is stable frame to frame, so a cluster does not re-form around a
    /// different seed and jump every time it is recomputed.
    /// </summary>
    public class UnitClusterLayer : MonoBehaviour
    {
        /// <summary>Raised with a cluster's members when its marker is clicked.</summary>
        public System.Action<List<UnitActor>> SelectRequested;

        /// <summary>Camera distance (m) at which clustering starts. Below this every unit draws itself.</summary>
        const float ClusterAltitudeM = 22000f;
        /// <summary>Screen radius, in reference pixels, within which same-side formations merge.</summary>
        const float ClusterRadiusPx = 78f;
        /// <summary>Fewer than this in range is not a crowd; those units keep their own counters.</summary>
        const int MinClusterSize = 3;
        /// <summary>Seconds between regrouping passes.</summary>
        const float RegroupSeconds = 0.35f;
        /// <summary>Marker diameter at the smallest and largest cluster sizes.</summary>
        const float MinMarkerPx = 46f, MaxMarkerPx = 86f;
        /// <summary>Cluster size the marker reaches <see cref="MaxMarkerPx"/> at.</summary>
        const float MarkerFullAtCount = 24f;
        /// <summary>Screen margin (px) outside which a cluster's marker is not drawn.</summary>
        const float OffscreenMarginPx = 120f;

        Camera _cam;
        CameraRig _rig;
        CombatSystem _combat;
        RectTransform _root;

        /// <summary>Live clusters. Members are re-read every frame, so a marker tracks a marching column.</summary>
        readonly List<Cluster> _clusters = new List<Cluster>();
        /// <summary>Markers built so far, reused rather than churned — this runs several times a second.</summary>
        readonly List<Marker> _pool = new List<Marker>();
        /// <summary>Units currently folded away, so the layer can put back exactly what it took.</summary>
        readonly HashSet<UnitActor> _hidden = new HashSet<UnitActor>();

        readonly List<UnitActor> _candidates = new List<UnitActor>();
        readonly List<Vector2> _screen = new List<Vector2>();
        readonly List<bool> _taken = new List<bool>();

        float _timer;
        bool _active;

        class Cluster
        {
            public readonly List<UnitActor> Members = new List<UnitActor>();
            public Team Team;
            public Vector2 Screen;
        }

        class Marker
        {
            public RectTransform Root;
            public Image Disc;
            public Image Ring;
            public Text Count;
            public Text Caption;
            public Button Button;
            public List<UnitActor> Members;
        }

        public void Build(Canvas canvas, Camera worldCam, CameraRig rig, CombatSystem combat)
        {
            _cam = worldCam;
            _rig = rig;
            _combat = combat;

            // Behind every other panel in the hierarchy so the rail, HUD and
            // right-hand panels all draw over the markers rather than under
            // them — a counter floating on top of the info panel reads as a bug.
            _root = UIFactory.CreateGroup(canvas.transform, "UnitClusters");
            UIFactory.Stretch(_root);
            _root.SetAsFirstSibling();
        }

        void LateUpdate()
        {
            if (_root == null || _cam == null) return;

            bool want = _combat != null && _combat.Running &&
                        _rig != null && _rig.DistanceMeters >= ClusterAltitudeM;

            if (!want)
            {
                if (_active) Disband();
                return;
            }

            _active = true;
            _timer -= Time.unscaledDeltaTime;
            if (_timer <= 0f)
            {
                _timer = RegroupSeconds;
                Regroup();
            }

            // Positions are refreshed every frame even though membership is not:
            // the camera moves continuously and a marker that only caught up
            // three times a second would visibly lag the ground under it.
            RefreshPositions();
        }

        // ------------------------------------------------------------ grouping

        void Regroup()
        {
            _candidates.Clear();
            _screen.Clear();
            _taken.Clear();

            float scale = ReferenceScale();

            foreach (var u in UnitRegistry.All)
            {
                if (u == null || !u.IsAlive || u.HiddenByFog) continue;

                Vector3 sp = _cam.WorldToScreenPoint(u.transform.position);
                // Behind the camera projects to a mirrored point in front of it;
                // clustering on that would group formations that are nowhere
                // near each other on screen.
                if (sp.z <= 0f) continue;

                _candidates.Add(u);
                _screen.Add(new Vector2(sp.x / scale, sp.y / scale));
                _taken.Add(false);
            }

            _clusters.Clear();
            float r2 = ClusterRadiusPx * ClusterRadiusPx;

            for (int i = 0; i < _candidates.Count; i++)
            {
                if (_taken[i]) continue;

                var seed = _candidates[i];
                var team = seed.State.TeamEnum;
                Cluster c = null;

                for (int j = i + 1; j < _candidates.Count; j++)
                {
                    if (_taken[j] || _candidates[j].State.TeamEnum != team) continue;
                    if ((_screen[j] - _screen[i]).sqrMagnitude > r2) continue;

                    if (c == null)
                    {
                        c = new Cluster { Team = team };
                        c.Members.Add(seed);
                    }
                    c.Members.Add(_candidates[j]);
                    _taken[j] = true;
                }

                if (c == null) continue;

                // Below the threshold the units are better off as themselves:
                // release the ones this pass claimed so a later seed can still
                // group them, and leave the seed unclaimed too.
                if (c.Members.Count < MinClusterSize)
                {
                    for (int k = 1; k < c.Members.Count; k++)
                        _taken[_candidates.IndexOf(c.Members[k])] = false;
                    continue;
                }

                _taken[i] = true;
                _clusters.Add(c);
            }

            ApplyHiding();
            SyncMarkers();
        }

        /// <summary>
        /// Hides exactly the units that are in a cluster this pass and restores
        /// everything that was hidden last pass and is not now. Tracked in a set
        /// rather than by walking the registry, so a unit hidden by fog or
        /// destroyed between passes is still released properly.
        /// </summary>
        void ApplyHiding()
        {
            var wanted = new HashSet<UnitActor>();
            foreach (var c in _clusters)
                foreach (var u in c.Members) wanted.Add(u);

            foreach (var u in _hidden)
                if (u != null && !wanted.Contains(u)) u.SetHiddenByCluster(false);

            foreach (var u in wanted)
                u.SetHiddenByCluster(true);

            _hidden.Clear();
            foreach (var u in wanted) _hidden.Add(u);
        }

        /// <summary>Puts every unit back and takes the markers down.</summary>
        void Disband()
        {
            _active = false;
            foreach (var u in _hidden)
                if (u != null) u.SetHiddenByCluster(false);
            _hidden.Clear();
            _clusters.Clear();
            // Null-guarded: this also runs from OnDisable during scene teardown,
            // by which point the markers may already have been destroyed.
            foreach (var m in _pool)
                if (m.Root != null) m.Root.gameObject.SetActive(false);
        }

        void OnDisable() => Disband();

        // ------------------------------------------------------------- markers

        void SyncMarkers()
        {
            while (_pool.Count < _clusters.Count) _pool.Add(BuildMarker(_pool.Count));

            for (int i = 0; i < _pool.Count; i++)
            {
                var m = _pool[i];
                if (i >= _clusters.Count) { m.Root.gameObject.SetActive(false); continue; }

                var c = _clusters[i];
                m.Members = c.Members;
                m.Root.gameObject.SetActive(true);

                Color side = c.Team == Team.User ? GameConfig.BlueTeam : GameConfig.RedTeam;
                m.Disc.color = new Color(side.r, side.g, side.b, 0.82f);
                m.Ring.color = new Color(1f, 1f, 1f, 0.85f);
                m.Count.text = c.Members.Count.ToString();
                m.Caption.text = Caption(c);
                m.Caption.color = side;

                float size = Mathf.Lerp(MinMarkerPx, MaxMarkerPx,
                    Mathf.Clamp01((c.Members.Count - MinClusterSize) /
                                  Mathf.Max(1f, MarkerFullAtCount - MinClusterSize)));
                m.Root.sizeDelta = new Vector2(size, size);
                m.Count.fontSize = Mathf.RoundToInt(size * 0.40f);
            }
        }

        /// <summary>
        /// One line under the count. The largest echelon present plus the head
        /// count is what a commander reads off a stack of counters — "how big is
        /// this and how many of them" — so that is what the marker says rather
        /// than a list of unit types nobody can fit in 80 pixels.
        /// </summary>
        static string Caption(Cluster c)
        {
            Echelon top = Echelon.Team;
            int men = 0;
            foreach (var u in c.Members)
            {
                if (u == null) continue;
                if (u.State.EchelonEnum > top) top = u.State.EchelonEnum;
                men += Mathf.RoundToInt(u.Def.manpower *
                    EchelonInfo.ManpowerMultiplier(u.State.EchelonEnum) * Mathf.Clamp01(u.State.strength));
            }
            return $"{top.ToString().ToUpperInvariant()}  ·  {men:n0}";
        }

        Marker BuildMarker(int index)
        {
            var m = new Marker();

            m.Root = UIFactory.CreateGroup(_root, "Cluster" + index);
            m.Root.anchorMin = m.Root.anchorMax = new Vector2(0.5f, 0.5f);
            m.Root.pivot = new Vector2(0.5f, 0.5f);

            // Outer ring first, disc over it: the ring is a hairline of clear
            // space around the fill so the marker reads against dark terrain and
            // bright terrain alike, the same job the unit captions' drop shadow
            // does.
            m.Ring = UIFactory.CreateImage(m.Root, UiIcons.Disc, "Ring");
            var ringRt = (RectTransform)m.Ring.transform;
            UIFactory.Stretch(ringRt);
            m.Ring.raycastTarget = false;

            m.Disc = UIFactory.CreateImage(m.Root, UiIcons.Disc, "Disc");
            var discRt = (RectTransform)m.Disc.transform;
            UIFactory.Stretch(discRt);
            discRt.offsetMin = new Vector2(3, 3);
            discRt.offsetMax = new Vector2(-3, -3);

            m.Count = UIFactory.CreateText(m.Root, "", 20, Color.white,
                TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Stretch(m.Count.rectTransform);
            m.Count.raycastTarget = false;

            m.Caption = UIFactory.CreateText(m.Root, "", UiTheme.FontLabel, UiTheme.Text,
                TextAnchor.UpperCenter, FontStyle.Bold);
            var cap = m.Caption.rectTransform;
            cap.anchorMin = new Vector2(0.5f, 0f);
            cap.anchorMax = new Vector2(0.5f, 0f);
            cap.pivot = new Vector2(0.5f, 1f);
            cap.anchoredPosition = new Vector2(0, -2);
            cap.sizeDelta = new Vector2(150, 16);
            m.Caption.raycastTarget = false;
            UIFactory.Fit(m.Caption, 9);

            // The disc is the click target: the whole marker stands in for the
            // formations under it, so clicking it selects them.
            m.Button = m.Disc.gameObject.AddComponent<Button>();
            m.Button.targetGraphic = m.Disc;
            var captured = m;
            m.Button.onClick.AddListener(() =>
            {
                if (captured.Members == null) return;
                var alive = new List<UnitActor>();
                foreach (var u in captured.Members)
                    if (u != null && u.IsAlive) alive.Add(u);
                if (alive.Count > 0) SelectRequested?.Invoke(alive);
            });

            return m;
        }

        // ----------------------------------------------------------- placement

        void RefreshPositions()
        {
            float scale = ReferenceScale();

            for (int i = 0; i < _clusters.Count && i < _pool.Count; i++)
            {
                var c = _clusters[i];
                var m = _pool[i];

                // Re-derived from the members every frame rather than cached, so
                // a marker rides a marching column instead of standing where the
                // column was when it was last regrouped.
                Vector2 sum = Vector2.zero;
                int n = 0;
                foreach (var u in c.Members)
                {
                    if (u == null || !u.IsAlive) continue;
                    Vector3 sp = _cam.WorldToScreenPoint(u.transform.position);
                    if (sp.z <= 0f) continue;
                    sum += new Vector2(sp.x, sp.y);
                    n++;
                }

                if (n == 0) { m.Root.gameObject.SetActive(false); continue; }

                Vector2 screen = sum / n;
                // Bounded by the camera's own pixel rect, not by the window.
                // The map is inset behind the editor's rail (CameraRig
                // .SetViewportLeftInset), so a column just off the left of the
                // picture projects into the strip the rail occupies — and a
                // marker parked on top of the nav is a marker pointing at ground
                // the player cannot see.
                var view = _cam.pixelRect;
                bool onScreen = screen.x > view.xMin - OffscreenMarginPx &&
                                screen.y > view.yMin - OffscreenMarginPx &&
                                screen.x < view.xMax + OffscreenMarginPx &&
                                screen.y < view.yMax + OffscreenMarginPx;
                m.Root.gameObject.SetActive(onScreen);
                if (!onScreen) continue;

                c.Screen = screen;
                // Canvas centre is the anchor, so the offset is measured from
                // the middle of the reference-resolution rect rather than from
                // the corner — that is what makes this correct at any window
                // size the CanvasScaler is matching.
                m.Root.anchoredPosition = new Vector2(
                    screen.x / scale - _root.rect.width * 0.5f,
                    screen.y / scale - _root.rect.height * 0.5f);
            }
        }

        /// <summary>
        /// Screen pixels per canvas unit. The canvas scales with the window, so
        /// raw screen coordinates have to be divided through by this before they
        /// mean anything in the layout — including the cluster radius, which is
        /// authored in reference pixels and must not tighten on a small window.
        /// </summary>
        float ReferenceScale()
        {
            var canvas = _root != null ? _root.GetComponentInParent<Canvas>() : null;
            return canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        }
    }
}
