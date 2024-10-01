﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using Inworld.UI;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Inworld.Sample
{
    public class ChatCanvas : PlayerCanvas
    {
        [Header("References")]
        [SerializeField] protected TMP_InputField m_InputField;
        [SerializeField] protected TMP_Dropdown m_Dropdown;
        [SerializeField] protected Button m_SendButton;
        [SerializeField] protected Button m_RecordButton;
        [SerializeField] protected BubblePanel m_BubblePanel;
        [SerializeField] protected string m_SubmitName;

        protected InputAction m_SubmitAction;
        protected CharSelectingMethod m_PrevSelectingMethod;
        protected override void Awake()
        {
            base.Awake();
            if (string.IsNullOrEmpty(m_SubmitName))
                return;
            m_SubmitAction = InworldAI.InputActions[m_SubmitName];
        }
        protected virtual void OnEnable()
        {
            InworldController.CharacterHandler.Event.onCharacterListJoined.AddListener(OnCharacterJoined);
            InworldController.CharacterHandler.Event.onCharacterListLeft.AddListener(OnCharacterLeft);
        }
        protected virtual void OnDisable()
        {
            if (!InworldController.Instance)
                return;
            InworldController.CharacterHandler.Event.onCharacterListJoined.RemoveListener(OnCharacterJoined);
            InworldController.CharacterHandler.Event.onCharacterListLeft.RemoveListener(OnCharacterLeft);
        }
        protected virtual void OnCharacterJoined(InworldCharacter newChar)
        {
            InworldAI.Log($"{newChar.Name} joined.");
            newChar.Event.onCharacterSelected.AddListener(OnCharSelected);
            newChar.Event.onCharacterDeselected.AddListener(OnCharDeselected);
            if (m_Dropdown)
            {
                m_Dropdown.options.Add(new TMP_Dropdown.OptionData
                {
                    text = newChar.Name
                });
                if (m_Dropdown.options.Count > 0)
                    m_Dropdown.gameObject.SetActive(true);
            }

        }
        
        protected virtual void OnCharacterLeft(InworldCharacter newChar)
        {
            InworldAI.Log($"{newChar.Name} left.");
            newChar.Event.onCharacterSelected.RemoveListener(OnCharSelected);
            newChar.Event.onCharacterDeselected.RemoveListener(OnCharDeselected);
            if (m_Dropdown)
            {
                TMP_Dropdown.OptionData option = m_Dropdown.options.FirstOrDefault(o => o.text == newChar.Name);
                if (option != null)
                    m_Dropdown.options.Remove(option);
                if (m_Dropdown.options.Count <= 0)
                    m_Dropdown.gameObject.SetActive(false);
            }

        }
        
        protected virtual void OnCharSelected(string newCharBrainName)
        {
            if (!m_Dropdown)
                return;
            string givenName = InworldController.CharacterHandler[newCharBrainName]?.Name;
            if (string.IsNullOrEmpty(givenName))
            {
                m_Dropdown.value = 0;
                m_Dropdown.RefreshShownValue();
            }
            else
            {
                int value = m_Dropdown.options.FindIndex(o => o.text == givenName);
                if (value == -1)
                    return;
                m_Dropdown.value = value;
                m_Dropdown.RefreshShownValue();
            }
            RefreshUIInteractive(true);
        }
        protected virtual void OnCharDeselected(string newCharBrainName)
        {
            if (!m_Dropdown)
                return;
            m_Dropdown.value = 0;
            m_Dropdown.RefreshShownValue();
            RefreshUIInteractive(false);
        }
        protected virtual void RefreshUIInteractive(bool isON)
        {
            if (m_InputField)
                m_InputField.interactable = isON;
            if (m_SendButton)
                m_SendButton.interactable = isON;
            if (m_RecordButton)
                m_RecordButton.interactable = isON;
        }
        protected override void OnCanvasOpen()
        {
            m_PrevSelectingMethod = InworldController.CharacterHandler.SelectingMethod;
            InworldController.CharacterHandler.SelectingMethod = CharSelectingMethod.Manual;
            if (m_BubblePanel)
                m_BubblePanel.UpdateContent();
        }
        protected override void OnCanvasClosed()
        {
            InworldController.CharacterHandler.SelectingMethod = m_PrevSelectingMethod;
        }
        /// <summary>
        /// Select the character by the default dropdown component.
        /// </summary>
        /// <param name="nIndex">the index in the drop down</param>
        public virtual void SelectCharacterByDropDown(int nIndex)
        {
            if (!m_Dropdown)
                return;
            if (nIndex < 0 || nIndex > m_Dropdown.options.Count)
                return;
            if (nIndex == 0) // NONE
            {
                InworldController.CharacterHandler.CurrentCharacter = null;
                return;
            }
            InworldCharacter character = InworldController.CharacterHandler.GetCharacterByGivenName(m_Dropdown.options[nIndex].text);
            if (!character || character == InworldController.CharacterHandler.CurrentCharacter)
                return;
            InworldController.CharacterHandler.CurrentCharacter = character;
        }
        public void Submit()
        {
            if (!m_InputField || string.IsNullOrEmpty(m_InputField.text))
                return;
            string text = m_InputField.text;
            if (text.StartsWith("*"))
                InworldController.Instance.SendNarrativeAction(text.Remove(0, 1));
            else
                InworldController.Instance.SendText(text);
            m_InputField.text = "";
        }
        protected override void HandleInput()
        {
            base.HandleInput();
            if (m_SubmitAction != null && m_SubmitAction.WasReleasedThisFrame())
                Submit();
        }
    }
}