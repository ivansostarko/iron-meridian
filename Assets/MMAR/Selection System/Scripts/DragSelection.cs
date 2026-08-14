using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MMAR.SelectionSystem
{
    public class DragSelection : MonoBehaviour
    {
        #region Border GameObjects
        [BoxGroup("Borders")]
        public RectTransform topBorder;
        [BoxGroup("Borders")]
        public RectTransform leftBorder;
        [BoxGroup("Borders")]
        public RectTransform rightBorder;
        [BoxGroup("Borders")]
        public RectTransform bottomBorder;
        #endregion
        #region UI Parameters
        [BoxGroup("UI Parameters")]
        public float borderWidth = 10;

        [BoxGroup("UI Parameters")]
        [OnValueChanged("UpdateImageColors")]
        [SerializeField] Color borderColor = Color.white;
        #endregion
        // Control Parameters
        [BoxGroup("Control Parameters")]
        public int mouseButtonIndex = 0;
        [BoxGroup("Control Parameters")]
        public float dragStartDelay = .15f;
        
        
        // Advance Properties
        [Foldout("Advance Properties")]
        [Tooltip("How many selectable object to process per frame")]
        [SerializeField] int perFrameProcess = 50;
        #region Working Variables        
        /// <summary>
        /// The  total collection of of <seealso cref="SelectableObject"/>
        /// </summary>
        [HideInInspector]
        public List<SelectableObject> selectableObjects = new List<SelectableObject>();
        /// <summary>
        /// The selected objects of <seealso cref="SelectableObject"/>
        /// </summary>
        List<SelectableObject> selectedObjects = new List<SelectableObject>();
        int curProcessingObjectIndex = 0;

        float lastClickTime = 0;
        bool startedDragging = false;
        Vector3 startingPosition;
        Vector2 maxPoint=new(), minPoint=new();
        #endregion
        public static DragSelection instance;
        private void Awake()
        {
            instance = this;
        }
        private void Start()
        {
            ClearBorders();
            UpdateImageColors();
        }
        #region Update Border Color
        public Color BorderColor { get { return borderColor; } set { borderColor = value; UpdateImageColors(); } }
        void UpdateImageColors()
        {
            UpdateRawImageColor(topBorder, borderColor);
            UpdateRawImageColor(leftBorder, borderColor);
            UpdateRawImageColor(rightBorder, borderColor);
            UpdateRawImageColor(bottomBorder, borderColor);
        }
        void UpdateRawImageColor(RectTransform rawImageRect, Color color)
        {
            rawImageRect.GetComponent<RawImage>().color = color;
        }
        #endregion
        void ClearBorders()
        {
            topBorder.sizeDelta = new Vector2(0, 0);
            leftBorder.sizeDelta = new Vector2(0, 0);
            rightBorder.sizeDelta = new Vector2(0, 0);
            bottomBorder.sizeDelta = new Vector2(0, 0);
        }
        void ResetSelectedObjects()
        {
            curProcessingObjectIndex = 0;
            selectedObjects.Clear();
        }
        bool isChoosingIenumeratorRunning = false,
            isFinalSelectionIenumeratorRunning=false;

        IEnumerator ChoosingIenumerator()
        {
            isChoosingIenumeratorRunning=true;
            while (curProcessingObjectIndex < selectableObjects.Count&&isChoosingIenumeratorRunning)
            {
                var curSelectAbleObject = selectableObjects[curProcessingObjectIndex];
                if(MathUtil.IsInRange(curSelectAbleObject.Position.x,minPoint.x,maxPoint.x) && MathUtil.IsInRange(curSelectAbleObject.Position.y, minPoint.y, maxPoint.y)){
                    curSelectAbleObject.OnPreSelected();
                }
                else
                {
                    curSelectAbleObject.OnDeselected();
                }
                curProcessingObjectIndex++;
                if (curProcessingObjectIndex>0 && curProcessingObjectIndex % perFrameProcess == 0)
                {
                    yield return new WaitForEndOfFrame();
                }
            }
            isChoosingIenumeratorRunning=false;
        }
        IEnumerator FinalSelectionIenumerator()
        {
            isFinalSelectionIenumeratorRunning = true;
            int curSelectedObjectIndex = 0;
            while (curSelectedObjectIndex < selectableObjects.Count && isFinalSelectionIenumeratorRunning)
            {
                var curSelectAbleObject = selectableObjects[curSelectedObjectIndex];
                if (MathUtil.IsInRange(curSelectAbleObject.Position.x, minPoint.x, maxPoint.x) && MathUtil.IsInRange(curSelectAbleObject.Position.y, minPoint.y, maxPoint.y))
                {
                    curSelectAbleObject.OnSelected();
                }
                else
                {
                    curSelectAbleObject.OnDeselected();
                }
                curSelectedObjectIndex++;
                if (curSelectedObjectIndex > 0 && curSelectedObjectIndex % perFrameProcess == 0)
                {
                    yield return new WaitForEndOfFrame();
                }
            }
            isFinalSelectionIenumeratorRunning = false;
        }
        public void Update()
        {
            if (startedDragging)
            {
                if (Input.GetMouseButtonUp(mouseButtonIndex))
                {
                    ClearBorders();
                    startedDragging = false;
                    lastClickTime = 0;
                    isChoosingIenumeratorRunning = false;
                    StartCoroutine(FinalSelectionIenumerator());
                }
                else
                {
                    isFinalSelectionIenumeratorRunning = false;
                    // Calculate the size of the selection box
                    var selectionWidth = startingPosition.x - Input.mousePosition.x;
                    var selectionHeight = startingPosition.y - Input.mousePosition.y;
                    // Resizing Borders
                    topBorder.sizeDelta = new Vector2(Mathf.Abs(selectionWidth), borderWidth);
                    bottomBorder.sizeDelta = new Vector2(Mathf.Abs(selectionWidth), borderWidth);
                    leftBorder.sizeDelta = new Vector2(borderWidth, Mathf.Abs(selectionHeight));
                    rightBorder.sizeDelta = new Vector2(borderWidth, Mathf.Abs(selectionHeight));
                    // Positioning Borders
                    topBorder.position = new(startingPosition.x - selectionWidth / 2, startingPosition.y);
                    bottomBorder.position = new(startingPosition.x - selectionWidth / 2, Input.mousePosition.y);
                    leftBorder.position = new(startingPosition.x, startingPosition.y - selectionHeight / 2);
                    rightBorder.position = new(Input.mousePosition.x, startingPosition.y - selectionHeight / 2);

                    #region Setting max min point
                    float x1 = startingPosition.x - selectionWidth;
                    float y1 = startingPosition.y - selectionHeight;
                    if (x1 > startingPosition.x)
                    {
                        maxPoint.x = x1;
                        minPoint.x = startingPosition.x;
                    }
                    else
                    {
                        maxPoint.x = startingPosition.x;
                        minPoint.x = x1;
                    }
                    if (y1 > startingPosition.y)
                    {
                        maxPoint.y = y1;
                        minPoint.y = startingPosition.y;
                    }
                    else
                    {
                        maxPoint.y = startingPosition.y;
                        minPoint.y = y1;
                    }
#endregion
                    ResetSelectedObjects();
                    // Running ienumerator
                    if (!isChoosingIenumeratorRunning)
                    {
                        StartCoroutine(ChoosingIenumerator());
                    }
                }
            }
            else
            {
                if (Input.GetMouseButtonDown(mouseButtonIndex))
                {
                    if (lastClickTime == 0)
                    {
                        lastClickTime = Time.time;
                    }
                }
            }

            if (lastClickTime != 0 && Time.time>lastClickTime+dragStartDelay) {
                
                startedDragging = true;
                startingPosition = Input.mousePosition;
                lastClickTime = 0;
            }
            else if(lastClickTime!=0 && Input.GetMouseButtonUp(mouseButtonIndex))
            {
                lastClickTime = 0;
            }
        }
    }
}