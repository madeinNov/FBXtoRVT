using System;

namespace FBXtoRVT.Core
{
    /// <summary>
    /// 응원 문구를 랜덤으로 골라주는 헬퍼.
    /// 1% 확률로 '당첨' 문구가 나온다.
    /// </summary>
    public static class CheerMessages
    {
        // 랜덤 생성기 (한 번만 만들어 재사용)
        private static readonly Random _rng = new Random();

        // 당첨 확률 (1%)
        private const double JackpotChance = 0.01;

        // 일반 응원 문구 ({0} 자리에 이름이 들어감)
        private static readonly string[] Normal =
        {
            "{0} 파이팅~",
            "{0} 힘내자!",
            "{0} 오늘도 수고 많았어!",
            "{0} 넌 할 수 있어 💪",
            "{0} 최고야, 최고!",
            "{0} 커피 한 잔의 여유를~",
            "{0} 퇴근까지 조금만 더!",
            "{0} 오늘의 주인공은 너!",
            "{0} 화이팅 넘치는 하루!",
            "{0} 넌 잘하고 있어, 걱정 마",
            "{0} 대박 나는 하루 되세요!",
        };

        // 1% 당첨 문구
        private static readonly string[] Jackpot =
        {
            "{0} 축! 당첨! 🎉",
            "{0} 커피 쿠폰 당첨! ☕ (기분만)",
        };

        /// <summary>
        /// 당첨 확률을 사람이 읽는 텍스트로 (예: "1%"). JackpotChance 와 항상 동기화됨.
        /// </summary>
        public static string JackpotPercentText => (JackpotChance * 100).ToString("0.#") + "%";

        /// <summary>
        /// 문구 선택 결과. 당첨 여부와 문구를 함께 담는다.
        /// </summary>
        public class Result
        {
            public string Message;   // 표시할 응원 문구
            public bool IsJackpot;   // 당첨(1%) 여부
        }

        /// <summary>
        /// 이름에 맞는 응원 문구를 랜덤으로 반환. 당첨 여부도 함께 알려준다.
        /// </summary>
        public static Result GetRandom(string name)
        {
            // 1% 확률로 당첨 문구, 아니면 일반 문구
            bool isJackpot = _rng.NextDouble() < JackpotChance;
            string[] pool = isJackpot ? Jackpot : Normal;

            string template = pool[_rng.Next(pool.Length)];
            return new Result
            {
                Message = string.Format(template, name),
                IsJackpot = isJackpot
            };
        }
    }
}
