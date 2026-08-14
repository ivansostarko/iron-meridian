
using UnityEngine;
namespace MMAR.SelectionSystem
{
    [RequireComponent(typeof(Outline))]
    public class SelectableObject : MonoBehaviour
    {
        [HideInInspector]
        public Outline outline;
        RectTransform rectTransform;
        // Start is called before the first frame update
        public virtual void Start()
        {
            rectTransform = GetComponent<RectTransform>();
            outline = GetComponent<Outline>();
            outline.enabled = false;
            if (DragSelection.instance != null)
            {
                DragSelection.instance.selectableObjects.Add(this);
            }

        }
        #region Selection Events
        public virtual void OnPreSelected()
        {
            outline.enabled = true;
        }
        public virtual void OnSelected()
        {
            outline.enabled = true;
        }
        public virtual void OnDeselected()
        {
            outline.enabled = false;
        }
        #endregion
        /// <summary>
        /// The screen position of the object
        /// </summary>
        public Vector3 Position
        {
            get
            {
                if (rectTransform == null)
                {
                    return Camera.main.WorldToScreenPoint(transform.position);
                }
                else
                {
                    return rectTransform.position;
                }
            }
        }
        public virtual void OnDestroy()
        {
            if (DragSelection.instance != null)
            {
                DragSelection.instance.selectableObjects.Remove(this);
            }
        }
    }
}