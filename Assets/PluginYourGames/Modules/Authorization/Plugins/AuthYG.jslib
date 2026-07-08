
mergeInto(LibraryManager.library,
{
	InitPlayer_js: function ()
	{
		var returnStr = playerData;
		var bufferSize = lengthBytesUTF8(returnStr) + 1;
		var buffer = _malloc(bufferSize);
		stringToUTF8(returnStr, buffer, bufferSize);
		return buffer;
	},
	
	OpenAuthDialog_js: function ()
	{
		OpenAuthDialog();
	},

	ShowAuthConsentPrompt_js: function (reasonPtr)
	{
		var reason = UTF8ToString(reasonPtr);
		showAuthPromptDom(reason);
	},

	IsAuthConsentPromptResolved_js: function ()
	{
		return authPromptResolved ? 1 : 0;
	},

	WasAuthConsentAccepted_js: function ()
	{
		return authPromptAccepted ? 1 : 0;
	},

	ResetAuthConsentPrompt_js: function ()
	{
		authPromptResolved = false;
		authPromptAccepted = false;
		hideAuthPromptDom();
	},
	
	RequestAuth_js: function () {
        InitPlayer();
    }
});