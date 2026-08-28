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
    ///   1.포어라인 : 포어라인 작업용 기능
    ///   2.SCR      : SCR 작업용 기능
    ///   공용       : 공정과 상관없이 쓰는 기능
    ///   기타       : 보조(뷰 조작) 기능
    ///   응원       : 응원 버튼
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
            CreateCommonPanel(application, assemblyPath);
            CreateEtcPanel(application, assemblyPath);
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
                "인식합니다. FLANGE 는 열린 커넥터 개수에 따라 'FLANGE 하' 또는 'FLANGE 상' 파라미터를 " +
                "해제한 뒤 연결하고, NUT 은 파라미터 변경 없이 연결합니다. (부품이 이동·회전)",
                "S", Colors.DarkSlateBlue);

            AddButton(panel, assemblyPath,
                "OverlapSelectButton",
                "겹침 객체\n선택",
                "FBXtoRVT.Commands.OverlapSelectCommand",
                "Main 객체의 바운딩 박스 안에 중심점이 들어가는(= 겹치는) Sub 객체를 선택합니다.",
                "실행하면 Main / Sub 문자열을 입력받아, 현재 뷰에서 이름에 Main 이 포함된 객체의 " +
                "바운딩 박스를 모으고, 이름에 Sub 가 포함된 객체의 중심점이 그 박스 안에 들어가면 " +
                "해당 Sub 객체를 선택합니다. 겹쳐서 남아 있는 객체를 한 번에 확인·정리할 때 씁니다.",
                "겹", Colors.SeaGreen);
        }

        /// <summary>
        /// "공용" 패널: 공정과 상관없이 쓰는 기능.
        /// </summary>
        private void CreateCommonPanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "공용");

            AddButton(panel, assemblyPath,
                "RightAnglePipeButton",
                "직각 배관\n생성기",
                "FBXtoRVT.Commands.RightAnglePipeCommand",
                "배관을 고른 뒤 대상 객체를 고르면, 그 배관에 직각으로 만나는 배관을 생성합니다.",
                "첫 번째로 기준 배관(배관만 선택 가능), 두 번째로 대상 객체(카테고리 제한 없음)를 " +
                "클릭합니다. 기준 배관의 중심선을 연장한 직선에 대상 객체의 기준점에서 수선의 발을 내리고, " +
                "'수선의 발 ~ 기준점' 을 잇는 배관을 기준 배관과 같은 타입·System Type·지름으로 만듭니다. " +
                "대상이 배관이면 기준점은 사용할 커넥터의 원점(닫힌 커넥터가 2개면 중단), " +
                "배관이 아니면 객체의 중심점입니다.",
                "직", Colors.SteelBlue);

            AddButton(panel, assemblyPath,
                "ElbowConnectButton",
                "ELBOW&\n배관/플랜지",
                "FBXtoRVT.Commands.ElbowConnectCommand",
                "엘보의 열린 커넥터 주변 20mm 안의 FLANGE / 배관 끝점을 찾아 연결합니다.",
                "현재 뷰에서 패밀리명에 'ELBOW' 가 포함되고 열린 커넥터가 있는 객체를 모아, 그 커넥터 " +
                "원점을 중심으로 한 변 20mm 짜리 박스를 만듭니다. 박스 안에 FLANGE 가 있으면 플랜지를 " +
                "이동·회전시켜 엘보에 연결하고, 열린 배관 끝점만 있으면 엘보를 이동·회전시켜 배관에 " +
                "연결합니다. 둘 다 있으면 플랜지를 먼저 붙인 뒤, 플랜지의 반대쪽 열린 커넥터를 배관에 " +
                "연결합니다.",
                "엘", Colors.Teal);

            AddButton(panel, assemblyPath,
                "HopperFlangeButton",
                "HOPPER&\n플랜지",
                "FBXtoRVT.Commands.HopperFlangeCommand",
                "HOPPER 안에 FLANGE 가 딱 1개일 때, 파라미터를 정리하고 HOPPER 를 연결합니다.",
                "현재 뷰에서 패밀리명에 'HOPPER' 가 포함된 객체의 바운딩 박스를 구하고, 그 안에 중심점이 " +
                "들어가는 FLANGE 가 정확히 1개일 때만 대상으로 인식합니다. HOPPER 에 가까운 쪽 플랜지 " +
                "커넥터가 Primary 인지에 따라 NW FLANGE 는 'FLANGE 하'/'FLANGE 상' 을, DC FLANGE 는 " +
                "'FLANGE 상'/'FLANGE 하' 를 해제합니다(BLIND FLANGE 는 변경 없음). 이어서 HOPPER 의 " +
                "Primary 가 아닌 커넥터를 그 플랜지 커넥터에 이동·회전으로 연결합니다.",
                "홉", Colors.Crimson);

            AddButton(panel, assemblyPath,
                "EquipmentFlangeNutButton",
                "장비&\n플랜지/NUT",
                "FBXtoRVT.Commands.EquipmentFlangeNutCommand",
                "Mechanical Equipment 안의 FLANGE / NUT 을 장비의 열린 커넥터에 연결합니다.",
                "SCR장비&플랜지/NUT 과 동일한 규칙이되, 대상이 'SCRUBBER' 패밀리가 아니라 " +
                "Mechanical Equipment 카테고리 전체입니다. 장비의 바운딩 박스를 모든 방향으로 20mm " +
                "확장한 뒤 그 안에서 'FLANGE' / 'NUT' 부품을 찾고, 부품 바운딩 박스 안에 장비의 열린 " +
                "커넥터가 정확히 1개 들어있으면 그 커넥터를 대상으로 인식합니다. FLANGE 는 열린 커넥터 " +
                "개수에 따라 'FLANGE 하' 또는 'FLANGE 상' 파라미터를 해제한 뒤 연결하고, NUT 은 파라미터 " +
                "변경 없이 연결합니다. (부품이 이동·회전)",
                "장", Colors.DarkCyan);

            AddButton(panel, assemblyPath,
                "FlexPipeButton",
                "Flex Pipe\n생성기",
                "FBXtoRVT.Commands.FlexPipeCommand",
                "첫 객체에서 두 번째 객체의 커넥터까지 FLEX PIPE 를 생성합니다.",
                "첫 번째, 두 번째 객체를 차례로 클릭하면 두 객체의 열린 커넥터 사이에 " +
                "'METAL HOSE_STS304(FLEX)' 타입의 FLEX PIPE 를 생성합니다. 지름과 System Type 은 " +
                "첫 객체(사용된 커넥터) 기준입니다. 둘 중 하나라도 열린 커넥터가 없으면 실행하지 않고, " +
                "한 객체에 열린 커넥터가 2개 이상이면 서로의 객체와 가장 가까운 커넥터 쌍을 사용합니다.",
                "F", Colors.OliveDrab);

            AddButton(panel, assemblyPath,
                "LinkVisibilityButton",
                "LINK\nON/OFF",
                "FBXtoRVT.Commands.LinkVisibilityCommand",
                "현재 뷰에서 링크된 RVT 모델(Coordination Model)의 가시성을 켜짐/꺼짐 토글합니다.",
                "실행할 때마다 현재 뷰의 'RVT Links' 카테고리 가시성을 켜짐 ↔ 꺼짐으로 전환합니다. " +
                "대화상자 없이 즉시 토글되므로, Revit 의 키보드 단축키(사용자 인터페이스 > 단축키)에 " +
                "등록해 반복적으로 켜고 끄는 용도로 씁니다.",
                "L", Colors.SlateGray);
        }

        /// <summary>
        /// "기타" 패널: 보조(뷰 조작) 기능.
        /// 프로젝트 공통 규칙(docs/PROJECT_RULES.md 규칙 2)에 따라 가장 오른쪽 패널로 둔다.
        /// </summary>
        private void CreateEtcPanel(UIControlledApplication application, string assemblyPath)
        {
            RibbonPanel panel = application.CreateRibbonPanel(TabName, "기타");

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
