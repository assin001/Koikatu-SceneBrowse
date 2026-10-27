using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KK_SceneBrowser;

public class CellClick : MonoBehaviour, IPointerClickHandler, IEventSystemHandler
{
	public int Index;

	public Action<int, bool, bool> LeftHandler;

	public Action<int> DoubleHandler;

	public Action<int> RightHandler;

	public void OnPointerClick(PointerEventData e)
	{
		if (e == null)
		{
			return;
		}
		if ((int)e.button == 1)
		{
			if (RightHandler != null)
			{
				RightHandler(Index);
			}
		}
		else
		{
			if ((int)e.button != 0)
			{
				return;
			}
			if (e.clickCount >= 2)
			{
				if (DoubleHandler != null)
				{
					DoubleHandler(Index);
				}
				return;
			}
			bool arg = Input.GetKey((KeyCode)306) || Input.GetKey((KeyCode)305);
			bool arg2 = Input.GetKey((KeyCode)304) || Input.GetKey((KeyCode)303);
			if (LeftHandler != null)
			{
				LeftHandler(Index, arg, arg2);
			}
		}
	}
}




