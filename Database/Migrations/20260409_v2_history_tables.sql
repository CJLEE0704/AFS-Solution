-- Schema Version: 2
-- Adds history/audit tables and indexing for SSOT queries.
CREATE TABLE IF NOT EXISTS schema_version (
  version_no INT NOT NULL PRIMARY KEY,
  applied_at DATETIME NOT NULL
);

CREATE TABLE IF NOT EXISTS pipe_stage_history (
  history_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  pipe_id VARCHAR(64) NOT NULL,
  project_id VARCHAR(64) NOT NULL,
  stage_id VARCHAR(64) NOT NULL,
  started_at DATETIME NOT NULL,
  ended_at DATETIME NULL,
  result VARCHAR(32) NOT NULL DEFAULT 'IN_PROGRESS',
  hold_reason_code VARCHAR(64) NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_stage_hist_proj_pipe (project_id, pipe_id, stage_id, started_at)
);

CREATE TABLE IF NOT EXISTS alarm_history (
  alarm_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  machine_id VARCHAR(64) NOT NULL,
  error_code VARCHAR(64) NOT NULL,
  message TEXT NULL,
  started_at DATETIME NOT NULL,
  cleared_at DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_alarm_machine_started (machine_id, started_at)
);

CREATE TABLE IF NOT EXISTS audit_log (
  audit_id BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY,
  user_id VARCHAR(64) NULL,
  action VARCHAR(100) NOT NULL,
  target VARCHAR(255) NULL,
  payload JSON NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  INDEX idx_audit_user_created (user_id, created_at)
);

ALTER TABLE pipes ADD COLUMN IF NOT EXISTS pipe_name VARCHAR(255) NULL;
ALTER TABLE production_records ADD COLUMN IF NOT EXISTS machine_id VARCHAR(64) NULL;
ALTER TABLE pipes ADD INDEX IF NOT EXISTS idx_pipes_project_status (project_id, status);
ALTER TABLE production_records ADD INDEX IF NOT EXISTS idx_prod_project_completed (project_id, completed_at);
ALTER TABLE pipe_machine_log ADD INDEX IF NOT EXISTS idx_pipe_machine_processed (pipe_id, machine_id, processed_at);

INSERT INTO schema_version(version_no, applied_at)
SELECT 2, NOW() WHERE NOT EXISTS (SELECT 1 FROM schema_version WHERE version_no=2);
