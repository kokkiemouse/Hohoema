﻿using NicoPlayerHohoema.Models;

namespace NicoPlayerHohoema.Services.Page
{
    public class KeywordSearchPagePayloadContent : VideoSearchOption
	{
		public override SearchTarget SearchTarget => SearchTarget.Keyword;
	}
}
