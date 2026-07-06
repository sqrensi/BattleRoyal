var playerData = NO_DATA;
var playerSignature = '';
let player = null;

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
    if (!ysdk) {
        LogStyledMessage('OpenAuthDialog: ysdk is null');
        return;
    }

    try {
        player = await ysdk.getPlayer({ signed: true });

        if (player.isAuthorized()) {
            await InitPlayer();
            YG2Instance('LoggedIn');
            return;
        }

        try {
            await ysdk.auth.openAuthDialog();
            player = await ysdk.getPlayer({ signed: true });

            if (player.isAuthorized()) {
                await InitPlayer();
                YG2Instance('LoggedIn');
            } else {
                await InitPlayer();
                LogStyledMessage('Authorization dialog closed, player is still unauthorized');
            }
        } catch (e) {
            await InitPlayer();
            LogStyledMessage('Authorization canceled or failed:', e?.message ?? e);
        }
    } catch (e) {
        player = null;
        playerSignature = '';
        await InitPlayer();
        LogStyledMessage('CRASH OpenAuthDialog / getPlayer:', e?.message ?? e);
    }
}
