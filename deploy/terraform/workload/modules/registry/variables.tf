variable "project_id" {
  type = string
}

variable "region" {
  type = string
}

variable "repository_id" {
  type        = string
  description = "Artifact Registry repository id (e.g. chimpout)."
  default     = "chimpout"
}

variable "labels" {
  type    = map(string)
  default = {}
}
