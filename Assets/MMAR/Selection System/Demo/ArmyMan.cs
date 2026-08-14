
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace MMAR.SelectionSystem.Demo
{
    [RequireComponent(typeof(NavMeshAgent))]
    public class ArmyMan : SelectableObject
    {
        NavMeshAgent agent;
        bool selected = false;

        public override void Start()
        {
            base.Start();
            agent = GetComponent<NavMeshAgent>();
        }
        public override void OnSelected()
        {
            base.OnSelected();
            selected = true;
        }
        public override void OnDeselected()
        {
            base.OnDeselected();
            selected = false;
        }
        private void Update()
        {
            if (selected && Input.GetMouseButtonDown(0))
            {
                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                // Perform the raycast and check if it hits any object with a collider
                if (Physics.Raycast(ray, out hit))
                {
                    agent.destination = hit.point;
                    // You can perform any action on the clicked object here
                }
            }
        }
    }
}