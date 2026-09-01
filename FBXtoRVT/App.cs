using System;
using System.Reflection;
using System.Windows.Media;
using Autodesk.Revit.UI;
using FBXtoRVT.Core;

namespace FBXtoRVT
{
    /// <summary>
    /// 애드인 시작 시 리본에 "FBXtoRVT" 탭과 기능 버튼을 생성하는 진입점.
    ///
    /// 패널 구성 (왼쪽 → 오른쪽)
    ///   1.포어라인     : 포어라인 작업용 기능
    ///   2.SCR          : SCR 작업용 기능
    ///   공용(연결)     : 부품을 장비/배관의 커넥터에 붙이는 기능
    ///   공용(배관)     : 배관을 새로 만드는 기능
    ///   공용(뷰/가시성): 화면에 무엇을 보여줄지 다루는 기능
    ///   응원           : 응원 버튼
    /// </summary>
    public class App : IExternalApplication
    {
        // 리본 탭 이름
        private const string TabName = "FBXtoRVT";

        public Result OnStartup(UIControlledApplication application)
        {
            // 1) 탭 생성 (이미 있으면 예외 무시)
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Exception)
            {
                // 같은 이름 탭이 이미 있으면 그대로 사용
            }

            // 2) 현재 이 DLL 의 경로 (버튼이 실행할 명령 어셈블리)
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            // ※ 패널 생성 순서 = 리본에 보이는 순서.
            //    패널 안에서는 AddButton 을 호출한 순서 = 버튼이 보이는 순서.
            CreatePoreLinePanel(application, assemblyPath);
            CreateScrPanel(application, assemblyPath);
            CreateCommonConnectPanel(application, assemblyPath);
            CreateCommonPipePanel(application, assemblyPath);
            CreateCommonViewPanel(application, assemblyPath);
            CreateCheerPanel(application, assemblyPath);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        // ===== 패널별 버튼 등록 =====

        /// <summary>
        /// "1.포어라인" 패널: 포어라인 작업에서 쓰는 기능.
        /// </summary>
        private void CreatePoreLinePanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "1.포어라인");

            AddButton(panel, assemblyPath,
                "SleeveAdjustButton",
                "타공 슬리브\n조정",
                "FBXtoRVT.Commands.SleeveAdjustCommand",
                "타공 SLEEVE 주변의 DC FLANGE 를 정리하고, 배관에 슬리브를 연결합니다.",
                "패밀리명에 '타공 SLEEVE' 가 포함된 객체의 바운딩 박스를 상부 100mm / 하부 2000mm 로 " +
                "확장한 뒤, 그 안에 중심점이 들어가는 'DC FLANGE' 중 상부면·하부면 중심점에 가장 가까운 " +
                "것을 각각 삭제합니다(최대 2개). 이어서 System Type 이 'Exhaust_Pumping' 인 배관 중 " +
                "끝점이 박스 안에 있는 배관을 찾아, 슬리브의 Primary 커넥터를 상부 배관 커넥터에 " +
                "이동·회전으로 연결하고, 남은 열린 커넥터를 하부 배관 커넥터에 연결합니다.",
                "타", Colors.Firebrick);

            AddButton(panel, assemblyPath,
                "DiagonalPipeButton",
                "대각 배관\n생성기",
                "FBXtoRVT.Commands.DiagonalPipeCommand",
                "평행한 두 배관을 클릭하면 45도 대각 배관을 생성합니다.",
                "첫 배관과 두 번째 배관을 차례로 클릭하면, 배관 방향으로부터 45도인 대각 배관을 " +
                "첫 배관과 동일한 타입·지름으로 생성합니다. 대각 배관의 양 끝점은 두 배관 중심선의 " +
                "연장선 위에 놓여 trim 이 가능하며, 두 중심점을 고려해 완만한 경사로 배치됩니다.",
                "대", Colors.DarkOrange);
        }

        /// <summary>
        /// "2.SCR" 패널: SCR 작업에서 쓰는 기능.
        /// </summary>
        private void CreateScrPanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "2.SCR");

            AddButton(panel, assemblyPath,
                "ScrubberFlangeButton",
                "SCR장비&\n플랜지/NUT",
                "FBXtoRVT.Commands.ScrubberFlangeCommand",
                "SCRUBBER 장비 안의 FLANGE / NUT 을 장비의 열린 커넥터에 연결합니다.",
                "패밀리명에 'SCRUBBER' 가 포함된 장비의 바운딩 박스 안에서 'FLANGE' / 'NUT' 부품을 찾고, " +
                "부품 바운딩 박스 안에 장비의 열린 커넥터가 정확히 1개 들어있으면 그 커넥터를 대상으로 " +
                "인식합니다. FLANGE 는 지금 붙이는 커넥터 쪽 플랜지('FLANGE 상' 또는 'FLANGE 하')를 " +
                "해제한 뒤 연결합니다. 어느 쪽인지는 패밀리 이름으로 정해집니다(NW=하, DC/BELLOWS=상, " +
                "BLIND 와 그 밖의 이름은 변경 없음). NUT 은 파라미터 변경 없이 연결합니다. (부품이 이동·회전)",
                "S", Colors.DarkSlateBlue);

            AddButton(panel, assemblyPath,
                "ElbowAdapterButton",
                "엘보 어댑터\n생성기",
                "FBXtoRVT.Commands.ElbowAdapterCommand",
                "양쪽이 연결된 엘보 조립품에서, 배관 쪽 ADAPTOR 파라미터를 켭니다.",
                "패밀리명에 'ASSEMBLY_ELBOW_ADPT_LOT-FLON' 이 포함된 엘보 조립품 중, End 커넥터가 " +
                "2개이고 둘 다 연결되어 있는 것만 대상으로 합니다. 엘보 중심점에서 가장 가까운 SCR 장비 " +
                "(패밀리명에 'SCRUBBER' 포함)를 그 엘보의 기준 장비로 삼고, 두 커넥터 중 그 장비 " +
                "중심점에서 더 먼 쪽을 고릅니다. 그 먼 쪽 커넥터가 배관과 연결되어 있으면, 해당 커넥터 " +
                "쪽 ADAPTOR 파라미터('ADAPTOR_상' 또는 'ADAPTOR_하')를 체크합니다. 이 패밀리는 " +
                "Primary 커넥터가 '상' 쪽이므로, 먼 쪽이 Primary 면 'ADAPTOR_상' 을 켭니다. " +
                "CLAMP 파라미터는 건드리지 않습니다.",
                "어", Colors.DarkGoldenrod);

            AddButton(panel, assemblyPath,
                "OverlapSelectButton",
                "겹침 객체\n선택",
                "FBXtoRVT.Commands.OverlapSelectCommand",
                "Main 객체의 바운딩 박스 안에 중심점이 들어가는(= 겹치는) Sub 객체를 선택합니다.",
                "실행하면 프리셋(ADPT / BELLOWS ...)을 고르거나 Main / Sub 문자열을 직접 입력받아, " +
                "현재 뷰에서 이름에 Main 이 포함된 객체의 바운딩 박스를 모으고, 이름에 Sub 가 포함된 " +
                "객체의 중심점이 그 박스 안에 들어가면 해당 Sub 객체를 선택합니다. Main 객체 자신은 " +
                "Sub 조건을 만족하더라도 선택하지 않습니다. 겹쳐서 남아 있는 객체를 한 번에 " +
                "확인·정리할 때 씁니다.",
                "겹", Colors.SeaGreen);
        }

        /// <summary>
        /// "공용(연결)" 패널: 부품을 장비/배관의 커넥터에 붙이는 기능.
        /// </summary>
        private void CreateCommonConnectPanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "공용(연결)");

            AddButton(panel, assemblyPath,
                "ElbowConnectButton",
                "ELBOW&\n배관/플랜지",
                "FBXtoRVT.Commands.ElbowConnectCommand",
                "엘보의 열린 커넥터 주변 60mm 안의 FLANGE / 배관 끝점을 찾아 연결합니다.",
                "현재 뷰에서 패밀리명에 'ELBOW' 가 포함되고 열린 커넥터가 있는 객체를 모아, 그 커넥터 " +
                "원점을 중심으로 한 변 60mm 짜리 박스를 만듭니다. 박스 안에 FLANGE 가 있으면 플랜지를 " +
                "이동·회전시켜 엘보에 연결하고, 열린 배관 끝점만 있으면 엘보를 이동·회전시켜 배관에 " +
                "연결합니다. 둘 다 있으면 플랜지를 먼저 붙인 뒤, 플랜지의 반대쪽 열린 커넥터를 배관에 " +
                "연결합니다.",
                "엘", Colors.Teal);

            AddButton(panel, assemblyPath,
                "HopperFlangeButton",
                "HOPPER&\n플랜지",
                "FBXtoRVT.Commands.HopperFlangeCommand",
                "HOPPER 안에 FLANGE 가 딱 1개일 때, 파라미터를 정리하고 HOPPER 를 연결합니다.",
                "현재 뷰에서 패밀리명에 'HOPPER' 가 포함된 객체의 바운딩 박스를 모든 방향으로 50mm 확장한 " +
                "뒤, 그 안에 중심점 또는 커넥터점이 들어가는 FLANGE 가 정확히 1개일 때만 대상으로 " +
                "인식합니다. HOPPER 의 모든 커넥터 굵기(ND)가 서로 같으면 플랜지의 'ND1' 값을 HOPPER 의 " +
                "'ND1' 에 넣습니다(50A/75A 처럼 서로 다르면 넣지 않습니다). 이어서 HOPPER 에 가까운 쪽 " +
                "플랜지 커넥터, 즉 지금 붙이는 커넥터 쪽 플랜지('FLANGE 상' 또는 'FLANGE 하')를 " +
                "해제합니다. 어느 쪽인지는 패밀리 이름으로 정해집니다(NW=하, DC/BELLOWS=상, BLIND 와 " +
                "그 밖의 이름은 변경 없음). 마지막으로 HOPPER 의 Primary 가 아닌 커넥터를 그 플랜지 " +
                "커넥터에 이동·회전으로 연결합니다.",
                "홉", Colors.Crimson);

            AddButton(panel, assemblyPath,
                "EquipmentFlangeNutButton",
                "장비&\n플랜지/NUT",
                "FBXtoRVT.Commands.EquipmentFlangeNutCommand",
                "Mechanical Equipment 안의 FLANGE / NUT 을 장비의 열린 커넥터에 연결합니다.",
                "SCR장비&플랜지/NUT 과 동일한 규칙이되, 대상이 'SCRUBBER' 패밀리가 아니라 " +
                "Mechanical Equipment 카테고리 전체입니다. 장비의 바운딩 박스를 모든 방향으로 20mm " +
                "확장한 뒤 그 안에서 'FLANGE' / 'NUT' 부품을 찾고, 부품 바운딩 박스 안에 장비의 열린 " +
                "커넥터가 정확히 1개 들어있으면 그 커넥터를 대상으로 인식합니다. FLANGE 는 지금 붙이는 " +
                "커넥터 쪽 플랜지('FLANGE 상' 또는 'FLANGE 하')를 해제한 뒤 연결합니다. 어느 쪽인지는 " +
                "패밀리 이름으로 정해집니다(NW=하, DC/BELLOWS=상, BLIND 와 그 밖의 이름은 변경 없음). " +
                "NUT 은 파라미터 변경 없이 연결합니다. (부품이 이동·회전)",
                "장", Colors.DarkCyan);
        }

        /// <summary>
        /// "공용(배관)" 패널: 배관을 새로 만드는 기능.
        /// </summary>
        private void CreateCommonPipePanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "공용(배관)");

            // "직각 배관 생성기" 는 사용하지 않기로 해서 버튼(아이콘)을 제거했다.
            // 기능 코드도 Commands/RightAnglePipeCommand.cs, Core/RightAnglePipeHelper.cs 에서
            // 통째로 주석 처리해 두었으므로, 되살리려면 그 두 파일의 주석을 풀고
            // 여기에 AddButton 을 다시 추가하면 된다.
            // (아래 "직각 배관 연결기" 는 이름은 비슷하지만 다른 기능이다.
            //  생성기는 "직각인 두 배관" 을, 연결기는 "평행한 두 배관" 을 대상으로 한다)

            AddButton(panel, assemblyPath,
                "RightAngleConnectButton",
                "직각 배관\n연결기",
                "FBXtoRVT.Commands.RightAngleConnectCommand",
                "평행한 두 배관을 차례로 클릭하면 직각 배관을 만들고 엘보까지 넣어 연결합니다.",
                "평행한 첫 번째 배관, 두 번째 배관을 차례로 클릭합니다. 두 배관 중심선의 공통수선을 " +
                "구해 그 자리에 직각(90도) 배관을 만들고, 양쪽에 엘보를 넣어 세 배관을 하나로 " +
                "연결합니다(대각 배관 생성기의 90도 버전이며, Trim 까지 대신 해 줍니다). 직각 배관은 " +
                "두 배관에서 서로 마주보는 쪽 끝의 가운데에 세우고, 각 배관의 끝을 그 자리까지 " +
                "늘리거나 줄입니다. 이어 붙일 쪽 커넥터에 캡·플랜지·기존 엘보 같은 부품이 붙어 " +
                "있으면 먼저 지웁니다. 배관 타입·System Type·지름은 항상 첫 번째로 고른 배관을 " +
                "따라갑니다. 두 배관이 평행하지 않거나 같은 직선 위에 있으면 실행하지 않습니다.",
                "연", Colors.SteelBlue);

            AddButton(panel, assemblyPath,
                "FlexPipeButton",
                "Flex Pipe\n생성기",
                "FBXtoRVT.Commands.FlexPipeCommand",
                "첫 객체에서 두 번째 객체의 커넥터까지 FLEX PIPE 를 생성합니다.",
                "첫 번째, 두 번째 객체를 차례로 클릭하면 두 객체의 열린 커넥터 사이에 " +
                "'METAL HOSE_STS304(FLEX)' 타입의 FLEX PIPE 를 생성합니다. 지름과 System Type 은 " +
                "첫 객체(사용된 커넥터) 기준이며, 첫 객체에 System Type 이 없으면 Undefined(미지정) " +
                "상태로 그대로 생성합니다. 둘 중 하나라도 열린 커넥터가 없으면 실행하지 않고, " +
                "한 객체에 열린 커넥터가 2개 이상이면 서로의 객체와 가장 가까운 커넥터 쌍을 사용합니다.",
                "F", Colors.OliveDrab);
        }

        /// <summary>
        /// "공용(뷰/가시성)" 패널: 화면에 무엇을 보여줄지 다루는 기능.
        /// </summary>
        private void CreateCommonViewPanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "공용(뷰/가시성)");

            AddButton(panel, assemblyPath,
                "LinkVisibilityButton",
                "LINK\nON/OFF",
                "FBXtoRVT.Commands.LinkVisibilityCommand",
                "좌표조정 모델(Coordination Model, nwc) 링크를 현재 뷰에서 보이게/안 보이게 토글합니다.",
                "Insert > Coordination Model 로 붙인 Navisworks 링크(.nwc/.nwd)가 대상이며, " +
                "RVT 링크는 건드리지 않습니다. 좌표조정 모델은 Revit 이 카테고리 숨기기나 객체 " +
                "숨기기를 허용하지 않으므로, 'FBXtoRVT 좌표조정모델 ON/OFF' 라는 선택 필터를 만들어 " +
                "현재 뷰에 걸고 그 필터의 가시성을 켜짐 ↔ 꺼짐으로 전환합니다(필터는 V/G 의 Filters " +
                "탭에 보이며, 지워도 다시 만들어집니다). V/G 의 'Coordination Models' 탭 체크는 켜진 " +
                "채로 남고 화면에만 안 보이게 되는 방식입니다. 대화상자 없이 즉시 토글되므로, " +
                "Revit 의 키보드 단축키(사용자 인터페이스 > 단축키)에 등록해 쓰면 편합니다.",
                "L", Colors.SlateGray);

            AddButton(panel, assemblyPath,
                "SectionBoxButton",
                "선택\nSection Box",
                "FBXtoRVT.Commands.SectionBoxCommand",
                "선택한 객체를 감싸는 Section Box 를 50mm 여유를 주어 3D 뷰에 적용합니다.",
                "객체를 선택한 상태에서 실행하면, 선택 객체들의 합쳐진 바운딩 박스에 " +
                "모든 방향으로 50mm 여유(tolerance)를 준 Section Box 를 현재 3D 뷰에 적용합니다. " +
                "3D 뷰에서만 동작합니다.",
                "박", Colors.MediumPurple);
        }

        /// <summary>
        /// "응원" 패널: 이름별 응원 버튼 8개.
        /// </summary>
        private void CreateCheerPanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel cheerPanel = application.CreateRibbonPanel(TabName, "응원");

            // (버튼에 보일 이름, 명령 클래스명, 아이콘 색)
            var cheerPeople = new (string name, string className, Color color)[]
            {
                ("유샘",   "CheerYusam",        Colors.Tomato),
                ("권순영", "CheerKwonSoonyoung", Colors.SteelBlue),
                ("최재원", "CheerChoiJaewon",    Colors.SeaGreen),
                ("김성민", "CheerKimSungmin",    Colors.DarkOrange),
                ("이종훈", "CheerLeeJonghun",    Colors.MediumPurple),
                ("문현국", "CheerMoonHyunguk",   Colors.Teal),
                ("고승희", "CheerKoSeunghee",    Colors.Crimson),
                ("정찬",   "CheerJungchan",     Colors.Goldenrod),
            };

            foreach (var person in cheerPeople)
            {
                PushButtonData cheerBtn = new PushButtonData(
                    "Btn_" + person.className,           // 내부 식별자
                    person.name,                          // 버튼 텍스트 = 이름
                    assemblyPath,
                    "FBXtoRVT.Commands." + person.className);

                cheerBtn.ToolTip = person.name + " 응원하기 (누르면 랜덤 응원 문구!)";

                // 아이콘: 사각 테두리 + 이름 첫 글자
                string firstChar = person.name.Substring(0, 1);
                cheerBtn.LargeImage = IconFactory.Create(32, firstChar, person.color);
                cheerBtn.Image = IconFactory.Create(16, firstChar, person.color);

                cheerPanel.AddItem(cheerBtn);
            }
        }

        // ===== 공통 =====

        /// <summary>
        /// 패널에 버튼 하나를 등록한다. (아이콘은 사각 테두리 + 글자로 코드에서 생성)
        /// </summary>
        /// <param name="buttonId">리본 내부 식별자(중복 불가)</param>
        /// <param name="buttonText">버튼에 보이는 텍스트</param>
        /// <param name="commandClassName">실행할 명령의 전체 클래스명</param>
        /// <param name="glyph">아이콘 가운데 글자</param>
        private void AddButton(RibbonPanel panel, string assemblyPath,
            string buttonId, string buttonText, string commandClassName,
            string toolTip, string longDescription, string glyph, Color color)
        {
            var data = new PushButtonData(buttonId, buttonText, assemblyPath, commandClassName);

            data.ToolTip = toolTip;
            data.LongDescription = longDescription;

            data.LargeImage = IconFactory.Create(32, glyph, color);
            data.Image = IconFactory.Create(16, glyph, color);

            panel.AddItem(data);
        }
    }
}
