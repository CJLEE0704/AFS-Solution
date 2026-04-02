using System;

namespace PipeBendingDashboard.Communication
{
    /// <summary>
    /// 머신 1대 상태 데이터 모델
    /// </summary>
    public class MachineStatus
    {
        public string MachineId   { get; set; } = "";
        public string MachineName { get; set; } = "";
        public bool   IsConnected { get; set; } = false;
        public string Status      { get; set; } = "READY";   // READY / WORKING / FINISH / ALARM
        public bool   HasAlarm    { get; set; } = false;
        public string ErrorCode   { get; set; } = "";
        public string LastEvent   { get; set; } = "";
        public double Oee         { get; set; } = 0;
        public double Speed       { get; set; } = 0;
        public string LastMessage { get; set; } = "—";
        public string Protocol    { get; set; } = "TCP/IP";
        public string IpAddress   { get; set; } = "";
        public int    Port        { get; set; } = 0;
        /// <summary>Ready 확인 완료 여부 (TCP 연결 후 READY? 명령 OK 응답)</summary>
        public bool   IsReady     { get; set; } = false;
        public DateTime LastReadyCheckedAtUtc { get; set; } = DateTime.MinValue;
    }

    /// <summary>
    /// 전체 라인 상태 (HTML로 전달하는 최상위 모델)
    /// </summary>
    public class AllMachineStatus
    {
        public MachineStatus Loader   { get; set; } = new() { MachineId = "LOADER",   MachineName = "Loader M/C"      };
        public MachineStatus Cutting  { get; set; } = new() { MachineId = "CUTTING",  MachineName = "Cutting M/C"     };
        public MachineStatus Laser    { get; set; } = new() { MachineId = "LASER",    MachineName = "Marking M/C"     };
        public MachineStatus Robot    { get; set; } = new() { MachineId = "ROBOT",    MachineName = "Moving Robot"    };
        public MachineStatus Bending  { get; set; } = new() { MachineId = "BENDING",  MachineName = "Bending M/C #1"  };
        public MachineStatus Bending2 { get; set; } = new() { MachineId = "BENDING2", MachineName = "Bending M/C #2"  };
    }

    /// <summary>
    /// HTML → C# 수신 명령 모델
    /// 예: {"type":"START","target":"BENDING"}
    /// </summary>
    public class WebCommand
    {
        public string Type   { get; set; } = "";  // START / STOP / STATUS / RESET
        public string Target { get; set; } = "";  // LOADER / CUTTING / LASER / ROBOT / BENDING / BENDING2
        public string Data   { get; set; } = "";  // 추가 데이터 (옵션)
    }
}
