using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KK_SceneBrowser;

public class ScrollCatcher : MonoBehaviour, IScrollHandler, IEventSystemHandler, IBeginDragHandler, IDragHandler
{
	public Action<float> WheelHandler;

	public Action<float> DragHandler;

	public void OnScroll(PointerEventData e)
	{
		if (WheelHandler != null && e != null)
		{
			WheelHandler(e.scrollDelta.y);
		}
	}

	public void OnBeginDrag(PointerEventData e)
	{
	}

	public void OnDrag(PointerEventData e)
	{
		if (DragHandler != null && e != null)
		{
			DragHandler(e.delta.y);
		}
	}
}




