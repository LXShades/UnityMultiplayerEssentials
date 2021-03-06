using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Extra optional tools for the Ticker timeline visualiser, with a ticker selection dropdown box and optional play/pause button
/// 
/// * Populates the dropdown with a selection of tickers in the game and reassigns the tickerTimeline's target when it changes.
/// * If isControllable is enabled, the target ticker may be played, paused, and seeked.
/// </summary>
[RequireComponent(typeof(Event))]
public class TickerTimelineDebugSelectorUI : MonoBehaviour
{
    public Dropdown dropdown;
    public TickerTimelineDebugUI tickerTimeline;

    [Header("Controllable")]
    [Tooltip("Whether the mouse and a play/pause button can be used to play, pause andor seek the selected ticker")]
    public bool isControllable = false;
    public Button playPauseButton;
    public Text playPauseButtonText;

    private readonly List<TickerBase> selectableTickers = new List<TickerBase>(32);

    private void Start()
    {
        // repopulate list when mouse interacts with the dropdown
        EventTrigger events = dropdown.gameObject.GetComponent<EventTrigger>();

        if (events == null)
            events = dropdown.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry eventHandler = new EventTrigger.Entry()
        {
            eventID = EventTriggerType.PointerEnter
        };
        eventHandler.callback.AddListener(OnDropdownEvent);

        events.triggers.Add(eventHandler);

        // handle when mouse clicks on timeline
        events = tickerTimeline.gameObject.GetComponent<EventTrigger>();

        if (events == null)
            events = tickerTimeline.gameObject.AddComponent<EventTrigger>();

        eventHandler = new EventTrigger.Entry()
        {
            eventID = EventTriggerType.Drag
        };
        eventHandler.callback.AddListener(OnTimelineDrag);

        events.triggers.Add(eventHandler);

        // handle when dropdown selection is changed
        dropdown.onValueChanged.AddListener(OnDropdownSelectionChanged);

        // play/pause feature
        if (playPauseButton)
            playPauseButton.onClick.AddListener(OnPlayPauseClicked);

        // give dropdown initial values
        RepopulateDropdown();
        // initial play/pause text
        UpdatePlayPauseButtonText();
    }

    private void OnEnable()
    {
        RepopulateDropdown();
    }

    private void RepopulateDropdown()
    {
        // preserve last selected item
        int newSelectedItemIndex = -1;

        List<Dropdown.OptionData> options = new List<Dropdown.OptionData>();
        dropdown.ClearOptions();
        selectableTickers.Clear();

        selectableTickers.Add(null);
        options.Add(new Dropdown.OptionData("<select ticker>"));

        // add new items
        foreach (WeakReference<TickerBase> tickerWeak in TickerBase.allTickers)
        {
            if (tickerWeak.TryGetTarget(out TickerBase ticker))
            {
                options.Add(new Dropdown.OptionData(ticker.targetName));
                selectableTickers.Add(ticker);

                if (tickerTimeline.targetTicker == ticker)
                    newSelectedItemIndex = options.Count - 1;
            }
        }

        // try to select last selected item if possible
        dropdown.options = options;
        dropdown.value = newSelectedItemIndex;
    }

    private void OnDropdownSelectionChanged(int value)
    {
        if (value > -1 && value < selectableTickers.Count)
        {
            tickerTimeline.targetTicker = selectableTickers[value];
            UpdatePlayPauseButtonText();
        }
    }

    private void OnTimelineDrag(BaseEventData eventData)
    {
        if (isControllable && tickerTimeline.targetTicker != null && tickerTimeline.targetTicker.isDebugPaused && eventData is PointerEventData pointerEvent)
        {
            // scroll target time
            if (pointerEvent.button == PointerEventData.InputButton.Left)
            {
                double timeDifference = tickerTimeline.timeline.timePerScreenX * pointerEvent.delta.x;
                double targetTime = tickerTimeline.targetTicker.playbackTime + timeDifference;

                if (timeDifference != 0f)
                {
                    tickerTimeline.targetTicker.SetDebugPaused(false); // briefly allow seek
                    tickerTimeline.targetTicker.Seek(targetTime, TickerSeekFlags.DebugMessages);
                    tickerTimeline.targetTicker.SetDebugPaused(true); // briefly allow seek
                }
            }
        }

        if (eventData is PointerEventData pointerEvent2)
        {
            // scroll source time
            if (pointerEvent2.button == PointerEventData.InputButton.Right)
            {
                double sourceTime = tickerTimeline.timeline.TimeAtScreenX(pointerEvent2.position.x);
                double targetTime = tickerTimeline.targetTicker.playbackTime;

                tickerTimeline.targetTicker.SetDebugPaused(false);
                tickerTimeline.targetTicker.Seek(sourceTime);
                tickerTimeline.targetTicker.stateTimelineBase.TrimAfter(sourceTime); // force reconfirmation
                tickerTimeline.targetTicker.Seek(targetTime);
                tickerTimeline.targetTicker.SetDebugPaused(true);
            }
        }
    }

    private void OnDropdownEvent(BaseEventData data)
    {
        RepopulateDropdown();
    }

    private void OnPlayPauseClicked()
    {
        if (isControllable && tickerTimeline.targetTicker != null)
            tickerTimeline.targetTicker.SetDebugPaused(!tickerTimeline.targetTicker.isDebugPaused);

        UpdatePlayPauseButtonText();
    }

    private void UpdatePlayPauseButtonText()
    {
        if (playPauseButtonText && tickerTimeline.targetTicker != null)
            playPauseButtonText.text = tickerTimeline.targetTicker.isDebugPaused ? ">" : "II";
    }

    private void OnValidate()
    {
        if (dropdown == null)
            dropdown = GetComponentInChildren<Dropdown>();

        if (tickerTimeline == null)
            tickerTimeline = GetComponentInChildren<TickerTimelineDebugUI>();

        if (isControllable && playPauseButton == null)
        {
            playPauseButton = GetComponentInChildren<Button>();
            if (playPauseButton)
                playPauseButtonText = playPauseButton.GetComponentInChildren<Text>();
        }
    }
}
