variable "project_id" {
  type = string
}

variable "region" {
  type = string
}

variable "repository_id" {
  type        = string
  description = "Artifact Registry repository id — always the env-scoped name_prefix (e.g. spacestroker-dev)."
}

variable "labels" {
  type    = map(string)
  default = {}
}
