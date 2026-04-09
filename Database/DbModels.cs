using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PipeBendingDashboard.Database
{
    // ── machines ──────────────────────────────────────────────
    [Table("machines")]
    public class MachineEntity
    {
        [Key][Column("machine_id")]   public string MachineId   { get; set; } = "";
        [Column("machine_name")]      public string MachineName { get; set; } = "";
        [Column("ip_address")]        public string IpAddress   { get; set; } = "";
        [Column("port")]              public int    Port        { get; set; } = 5000;
        [Column("protocol")]          public string Protocol    { get; set; } = "TCP/IP";
        [Column("is_active")]         public bool   IsActive    { get; set; } = true;
        [Column("sort_order")]        public int?   SortOrder   { get; set; }
        [Column("created_at")]        public DateTime CreatedAt { get; set; } = DateTime.Now;
        [Column("updated_at")]        public DateTime? UpdatedAt { get; set; }
    }

    // ── machine_status_log ────────────────────────────────────
    [Table("machine_status_log")]
    public class MachineStatusLogEntity
    {
        [Key][Column("log_id")]       public long    LogId       { get; set; }
        [Column("machine_id")]        public string  MachineId   { get; set; } = "";
        [Column("status")]            public string  Status      { get; set; } = "IDLE";
        [Column("is_connected")]      public bool    IsConnected { get; set; }
        [Column("is_ready")]          public bool    IsReady     { get; set; }
        [Column("oee")]               public decimal? Oee        { get; set; }
        [Column("speed")]             public decimal? Speed      { get; set; }
        [Column("has_alarm")]         public bool    HasAlarm    { get; set; }
        [Column("last_message")]      public string? LastMessage { get; set; }
        [Column("logged_at")]         public DateTime LoggedAt   { get; set; }
        [Column("created_at")]        public DateTime CreatedAt  { get; set; } = DateTime.Now;
    }

    // ── comm_log ──────────────────────────────────────────────
    [Table("comm_log")]
    public class CommLogEntity
    {
        [Key][Column("log_id")]       public long    LogId     { get; set; }
        [Column("machine_id")]        public string? MachineId { get; set; }
        [Column("log_type")]          public string  LogType   { get; set; } = "";
        [Column("command")]           public string? Command   { get; set; }
        [Column("response")]          public string? Response  { get; set; }
        [Column("is_success")]        public bool?   IsSuccess { get; set; }
        [Column("logged_at")]         public DateTime LoggedAt { get; set; }
        [Column("created_at")]        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    // ── projects ──────────────────────────────────────────────
    [Table("projects")]
    public class ProjectEntity
    {
        [Key][Column("project_id")]   public string  ProjectId   { get; set; } = "";
        [Column("project_name")]      public string  ProjectName { get; set; } = "";
        [Column("file_type")]         public string  FileType    { get; set; } = "JSON";
        [Column("site_name")]         public string? SiteName    { get; set; }
        [Column("description")]       public string? Description { get; set; }
        [Column("status")]            public string  Status      { get; set; } = "ACTIVE";
        [Column("added_at")]          public DateTime AddedAt    { get; set; }
        [Column("created_at")]        public DateTime CreatedAt  { get; set; } = DateTime.Now;
        [Column("updated_at")]        public DateTime? UpdatedAt { get; set; }
    }

    // ── pipes ─────────────────────────────────────────────────
    [Table("pipes")]
    public class PipeEntity
    {
        [Key][Column("pipe_id")]      public string  PipeId      { get; set; } = "";
        [Column("project_id")]        public string  ProjectId   { get; set; } = "";
        [Column("proj_name")]         public string  ProjName    { get; set; } = "";
        [Column("pipe_name")]         public string? PipeName    { get; set; }
        [Column("material")]          public string  Material    { get; set; } = "SS400";
        [Column("size")]              public int     Size        { get; set; } = 32;
        [Column("total_length")]      public int?    TotalLength { get; set; }
        [Column("shape_name")]        public string? ShapeName   { get; set; }
        [Column("bends")]             public int?    Bends       { get; set; }
        [Column("status")]            public string  Status      { get; set; } = "미완료";
        [Column("deadline")]          public DateTime? Deadline  { get; set; }
        [Column("sort_order")]        public int?    SortOrder   { get; set; }
        [Column("created_at")]        public DateTime CreatedAt  { get; set; } = DateTime.Now;
        [Column("updated_at")]        public DateTime? UpdatedAt { get; set; }
    }

    // ── production_records ────────────────────────────────────
    [Table("production_records")]
    public class ProductionRecordEntity
    {
        [Key][Column("record_id")]    public long    RecordId    { get; set; }
        [Column("pipe_id")]           public string  PipeId      { get; set; } = "";
        [Column("project_id")]        public string  ProjectId   { get; set; } = "";
        [Column("machine_id")]        public string? MachineId   { get; set; }
        [Column("station_id")]        public string? StationId   { get; set; }
        [Column("operator_id")]       public string? OperatorId  { get; set; }
        [Column("config_id")]         public string  ConfigId    { get; set; } = "";
        [Column("result")]            public string  Result      { get; set; } = "완료";
        [Column("defect_type")]       public string? DefectType  { get; set; }
        [Column("cycle_time_s")]      public int?    CycleTimeS  { get; set; }
        [Column("oee_snapshot")]      public decimal? OeeSnapshot { get; set; }
        [Column("completed_at")]      public DateTime CompletedAt { get; set; }
        [Column("created_at")]        public DateTime CreatedAt   { get; set; } = DateTime.Now;
    }

    [Table("pipe_stage_history")]
    public class PipeStageHistoryEntity
    {
        [Key][Column("history_id")]       public long HistoryId       { get; set; }
        [Column("pipe_id")]               public string PipeId         { get; set; } = "";
        [Column("project_id")]            public string ProjectId      { get; set; } = "";
        [Column("stage_id")]              public string StageId        { get; set; } = "";
        [Column("started_at")]            public DateTime StartedAt    { get; set; }
        [Column("ended_at")]              public DateTime? EndedAt     { get; set; }
        [Column("result")]                public string Result          { get; set; } = "IN_PROGRESS";
        [Column("hold_reason_code")]      public string? HoldReasonCode { get; set; }
        [Column("created_at")]            public DateTime CreatedAt     { get; set; } = DateTime.Now;
    }

    [Table("alarm_history")]
    public class AlarmHistoryEntity
    {
        [Key][Column("alarm_id")]         public long AlarmId          { get; set; }
        [Column("machine_id")]            public string MachineId      { get; set; } = "";
        [Column("error_code")]            public string ErrorCode      { get; set; } = "";
        [Column("message")]               public string? Message       { get; set; }
        [Column("started_at")]            public DateTime StartedAt    { get; set; }
        [Column("cleared_at")]            public DateTime? ClearedAt   { get; set; }
        [Column("created_at")]            public DateTime CreatedAt    { get; set; } = DateTime.Now;
    }

    [Table("audit_log")]
    public class AuditLogEntity
    {
        [Key][Column("audit_id")]         public long AuditId          { get; set; }
        [Column("user_id")]               public string? UserId        { get; set; }
        [Column("action")]                public string Action         { get; set; } = "";
        [Column("target")]                public string? Target        { get; set; }
        [Column("payload")]               public string? Payload       { get; set; }
        [Column("created_at")]            public DateTime CreatedAt    { get; set; } = DateTime.Now;
    }

    // ── pipe_machine_log — 배관별 머신 처리 이력 ─────────────
    [Table("pipe_machine_log")]
    public class PipeMachineLogEntity
    {
        [Key][Column("log_id")]       public long     LogId        { get; set; }
        [Column("pipe_id")]           public string   PipeId       { get; set; } = "";
        [Column("project_id")]        public string   ProjectId    { get; set; } = "";
        [Column("machine_id")]        public string   MachineId    { get; set; } = "";  // LOADER/CUTTING/LASER/ROBOT/BENDING/BENDING2
        [Column("machine_name")]      public string?  MachineName  { get; set; }
        [Column("config_id")]         public string?  ConfigId     { get; set; }
        [Column("operator_id")]       public string?  OperatorId   { get; set; }
        [Column("result")]            public string   Result       { get; set; } = "완료";  // 완료/불량
        [Column("cycle_time_s")]      public int?     CycleTimeS   { get; set; }
        [Column("processed_at")]      public DateTime ProcessedAt  { get; set; }
        [Column("created_at")]        public DateTime CreatedAt    { get; set; } = DateTime.Now;
    }

    // ── users ─────────────────────────────────────────────────
    [Table("users")]
    public class UserEntity
    {
        [Key][Column("user_id")]      public string  UserId       { get; set; } = "";
        [Column("user_name")]         public string  UserName     { get; set; } = "";
        [Column("password_hash")]     public string  PasswordHash { get; set; } = "";
        [Column("role")]              public string  Role         { get; set; } = "worker";
        [Column("is_fixed")]          public bool    IsFixed      { get; set; }
        [Column("is_active")]         public bool    IsActive     { get; set; } = true;
        [Column("last_login")]        public DateTime? LastLogin  { get; set; }
        [Column("created_at")]        public DateTime CreatedAt   { get; set; } = DateTime.Now;
        [Column("updated_at")]        public DateTime? UpdatedAt  { get; set; }
    }
}
