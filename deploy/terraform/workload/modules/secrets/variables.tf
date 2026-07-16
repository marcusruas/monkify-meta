variable "project_id" {
  type = string
}

variable "secret_ids" {
  type        = set(string)
  description = "Secret Manager secret ids to create as empty containers (values synced by the deploy tool)."
}

variable "accessor_members" {
  type        = map(list(string))
  description = "Map of secret_id -> list of member principals (e.g. serviceAccount:...) granted accessor."
  default     = {}
}

variable "labels" {
  type    = map(string)
  default = {}
}
