﻿/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

using System;
using System.Collections.Generic;

namespace Inworld.Entities
{

	[Serializable]
	public class Feedback
	{
		//public FeedbackData interactionFeedback;
		public bool isLike;
		public List<string> type;
		public string comment;
		public string name;
		public string InteractionID { get; set; }
		
		public string CorrelationID { get; set; }

		public Feedback(bool like, string interaction, string correlation, string commentText, List<string> dislikes = null)
		{
			InteractionID = interaction;
			CorrelationID = correlation;
			//interactionFeedback = new FeedbackData(like, commentText, dislikes);
			isLike = like;
			comment = commentText;
			type = new List<string>();
			if (dislikes != null && dislikes.Count != 0)
				type = dislikes;
		}
		public void SetCallbackReference(string callbackRef)
		{
			// if (interactionFeedback == null)
			// 	return;
			// interactionFeedback.name = callbackRef;
		}
		
	}
	[Serializable]
	public class FeedbackData
	{
		public bool isLike;
		public List<string> type;
		public string comment;
		public string name;

		public FeedbackData(bool like, string text, List<string> dislikes = null)
		{
			isLike = like;
			comment = text;
			type = new List<string>();
			if (dislikes != null && dislikes.Count != 0)
				type = dislikes;
		}
	}
}
