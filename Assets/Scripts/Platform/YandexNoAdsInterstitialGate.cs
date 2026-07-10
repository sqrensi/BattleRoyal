using ShooterPrototype.Player;

namespace YG
{
    public partial class YG2
    {
        public static System.Func<bool> CustomInterstitialBlockCheck;

        [InitYG]
        private static void InitNoAdsInterstitialGate()
        {
            CustomInterstitialBlockCheck = () => PlayerProfileService.HasNoAdsPass;
        }
    }
}
