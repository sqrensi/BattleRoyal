var playerData = NO_DATA;
var playerSignature = '';
let player = null;
let authPromptRoot = null;
let authPromptResolved = false;
let authPromptAccepted = false;

function ensureAuthPromptDom() {
    if (authPromptRoot) {
        return authPromptRoot;
    }

    authPromptRoot = document.createElement('div');
    authPromptRoot.id = 'yg-auth-prompt-overlay';
    authPromptRoot.style.position = 'fixed';
    authPromptRoot.style.left = '0';
    authPromptRoot.style.top = '0';
    authPromptRoot.style.width = '100vw';
    authPromptRoot.style.height = '100vh';
    authPromptRoot.style.display = 'none';
    authPromptRoot.style.alignItems = 'center';
    authPromptRoot.style.justifyContent = 'center';
    authPromptRoot.style.background = 'rgba(0, 0, 0, 0.72)';
    authPromptRoot.style.zIndex = '2147483647';
    authPromptRoot.style.padding = '24px';
    authPromptRoot.style.boxSizing = 'border-box';

    const panel = document.createElement('div');
    panel.style.width = 'min(560px, calc(100vw - 32px))';
    panel.style.maxHeight = 'calc(100vh - 32px)';
    panel.style.overflow = 'auto';
    panel.style.background = 'linear-gradient(180deg, rgba(27,24,20,0.98), rgba(19,17,14,0.98))';
    panel.style.border = '1px solid rgba(153, 123, 46, 0.9)';
    panel.style.boxShadow = '0 18px 52px rgba(0,0,0,0.42)';
    panel.style.borderRadius = '14px';
    panel.style.padding = '24px';
    panel.style.fontFamily = 'Arial, sans-serif';
    panel.style.color = '#f2eadb';

    const title = document.createElement('div');
    title.textContent = 'Вход через Яндекс ID';
    title.style.fontSize = '24px';
    title.style.fontWeight = '700';
    title.style.textAlign = 'center';
    title.style.marginBottom = '16px';

    const reason = document.createElement('div');
    reason.id = 'yg-auth-prompt-reason';
    reason.style.fontSize = '16px';
    reason.style.lineHeight = '1.45';
    reason.style.whiteSpace = 'pre-wrap';
    reason.style.marginBottom = '20px';

    const buttons = document.createElement('div');
    buttons.style.display = 'flex';
    buttons.style.gap = '12px';
    buttons.style.justifyContent = 'center';
    buttons.style.flexWrap = 'wrap';

    const accept = document.createElement('button');
    accept.id = 'yg-auth-prompt-accept';
    accept.textContent = 'Войти через Яндекс';
    accept.style.minWidth = '210px';
    accept.style.height = '46px';
    accept.style.border = '1px solid #7a5615';
    accept.style.borderRadius = '10px';
    accept.style.cursor = 'pointer';
    accept.style.fontWeight = '700';
    accept.style.fontSize = '16px';
    accept.style.color = '#2f240e';
    accept.style.background = 'linear-gradient(180deg, #f0c64a, #c18b1c)';

    const decline = document.createElement('button');
    decline.id = 'yg-auth-prompt-decline';
    decline.textContent = 'Продолжить без входа';
    decline.style.minWidth = '210px';
    decline.style.height = '46px';
    decline.style.border = '1px solid rgba(255,255,255,0.16)';
    decline.style.borderRadius = '10px';
    decline.style.cursor = 'pointer';
    decline.style.fontWeight = '700';
    decline.style.fontSize = '16px';
    decline.style.color = '#efe5d4';
    decline.style.background = '#24211c';

    decline.onclick = () => {
        authPromptAccepted = false;
        authPromptResolved = true;
        hideAuthPromptDom();
    };

    accept.onclick = async () => {
        accept.disabled = true;
        decline.disabled = false;
        accept.style.opacity = '0.7';
        reason.textContent = 'Открываем авторизацию Яндекс...';

        try {
            authPromptAccepted = await openAuthDialogDirect();
        } finally {
            authPromptResolved = true;
            hideAuthPromptDom();
            accept.disabled = false;
            accept.style.opacity = '1';
        }
    };

    buttons.appendChild(accept);
    buttons.appendChild(decline);
    panel.appendChild(title);
    panel.appendChild(reason);
    panel.appendChild(buttons);
    authPromptRoot.appendChild(panel);
    document.body.appendChild(authPromptRoot);

    return authPromptRoot;
}

function hideAuthPromptDom() {
    if (authPromptRoot) {
        authPromptRoot.style.display = 'none';
    }
}

function showAuthPromptDom(reasonText) {
    const root = ensureAuthPromptDom();
    const reason = root.querySelector('#yg-auth-prompt-reason');
    if (reason) {
        reason.textContent = reasonText || '';
    }
    authPromptResolved = false;
    authPromptAccepted = false;
    root.style.display = 'flex';
    LogStyledMessage('YG auth prompt shown');
}

async function openAuthDialogDirect() {
    if (!ysdk) {
        LogStyledMessage('OpenAuthDialogDirect: ysdk is null');
        return false;
    }

    try {
        LogStyledMessage('OpenAuthDialogDirect: start');
        player = await ysdk.getPlayer({ signed: true });

        if (player && player.isAuthorized()) {
            LogStyledMessage('OpenAuthDialogDirect: already authorized');
            await InitPlayer();
            YG2Instance('LoggedIn');
            return true;
        }

        LogStyledMessage('OpenAuthDialogDirect: calling ysdk.auth.openAuthDialog()');
        await ysdk.auth.openAuthDialog();
        player = await ysdk.getPlayer({ signed: true });
        await InitPlayer();

        if (player && player.isAuthorized()) {
            LogStyledMessage('OpenAuthDialogDirect: success');
            YG2Instance('LoggedIn');
            return true;
        }

        LogStyledMessage('Authorization dialog closed, player is still unauthorized');
        return false;
    } catch (e) {
        await InitPlayer();
        LogStyledMessage('Authorization canceled or failed:', e?.message ?? e);
        return false;
    }
}

async function resolvePlayerSignature(signedPlayer) {
    if (!signedPlayer) {
        return '';
    }

    if (typeof signedPlayer.signature === 'string' && signedPlayer.signature.length > 0) {
        return signedPlayer.signature;
    }

    if (typeof signedPlayer.fetch === 'function') {
        try {
            const fetched = await signedPlayer.fetch();
            if (fetched && typeof fetched.signature === 'string' && fetched.signature.length > 0) {
                return fetched.signature;
            }
        } catch (e) {
            LogStyledMessage('resolvePlayerSignature fetch failed:', e?.message ?? e);
        }
    }

    return '';
}

async function InitPlayer() {
    try {
        if (!ysdk) {
            return Final(NotAuthorized(false));
        }

        player = await ysdk.getPlayer({ signed: true });
        playerSignature = '';

        if (player && player.isAuthorized()) {
            playerSignature = await resolvePlayerSignature(player);
        }

        if (!player || !player.isAuthorized()) {
            return Final(NotAuthorized(true));
        }

        const authJson = {
            playerAuth: "resolved",
            playerName: player.getName(),
            playerId: player.getUniqueID(),
            playerPhoto: player.getPhoto('___photoSize___'),
            payingStatus: player.getPayingStatus(),
            playerSignature: playerSignature || ''
        };

        return Final(JSON.stringify(authJson));
    } catch (e) {
        console.error('CRASH InitPlayer:', e?.message ?? e);
        player = null;
        playerSignature = '';
        return Final(NotAuthorized(false));
    }

    function Final(res) {
        playerData = res;
        YG2Instance('SetAuth', res);
        return res;
    }
}

function NotAuthorized(hasPlayer = false) {
    playerSignature = '';
    const authJson = {
        playerAuth: "rejected",
        playerName: "unauthorized",
        playerId: hasPlayer && player ? player.getUniqueID() : "unauthorized",
        playerPhoto: "no data",
        payingStatus: "unknown",
        playerSignature: ''
    };

    return JSON.stringify(authJson);
}

async function OpenAuthDialog() {
    return openAuthDialogDirect();
}
