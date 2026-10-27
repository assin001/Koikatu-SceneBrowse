using System;
using UnityEngine;
using Object = UnityEngine.Object;
using UnityEngine.EventSystems;

namespace KK_SceneBrowser;

public class ScrollTrackDrag : MonoBehaviour, IPointerDownHandler, IEventSystemHandler, IDragHandler
{
	public Action<float> OnScrollTo;

	public RectTransform Track;

	private void Fire(PointerEventData e)
	{
		Vector2 val = default(Vector2);
		if (!((Object)(object)Track == (Object)null) && OnScrollTo != null && e != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(Track, e.position, (Camera)null, out val))
		{
			Rect rect = Track.rect;
			float height = rect.height;
			if (!(height <= 0.001f))
			{
				float num = (height / 2f - val.y) / height;
				OnScrollTo(Mathf.Clamp01(num));
			}
		}
	}

	public void OnPointerDown(PointerEventData e)
	{
		Fire(e);
	}

	public void OnDrag(PointerEventData e)
	{
		Fire(e);
	}
}




