using System;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.EventSystems;

namespace KK_SceneBrowser;

public class SliderDrag : MonoBehaviour, IScrollHandler, IEventSystemHandler, IPointerDownHandler, IDragHandler
{
	public Action<float> OnValue;

	public RectTransform Track;

	public float Width = 132f;

	public float Radius = 8f;

	private float ToValue(Vector2 screenPos)
	{
		if ((Object)(object)Track == (Object)null)
		{
			return 0f;
		}
		Vector2 val = default(Vector2);
		if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Track, screenPos, (Camera)null, out val))
		{
			return 0f;
		}
		float num = Width - Radius * 2f;
		if (num <= 0.001f)
		{
			return 0f;
		}
		return Mathf.Clamp01((val.x + Width / 2f - Radius) / num);
	}

	public void OnPointerDown(PointerEventData e)
	{
		if (e != null && OnValue != null)
		{
			OnValue(ToValue(e.position));
		}
	}

	public void OnDrag(PointerEventData e)
	{
		if (e != null && OnValue != null)
		{
			OnValue(ToValue(e.position));
		}
	}

	public void OnScroll(PointerEventData e)
	{
		if (e != null && OnValue != null)
		{
			float num = ToValue(new Vector2(Input.mousePosition.x, Input.mousePosition.y));
			OnValue(Mathf.Clamp01(num + e.scrollDelta.y * 0.06f));
		}
	}
}




