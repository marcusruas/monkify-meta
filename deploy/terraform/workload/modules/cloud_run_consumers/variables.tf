variable "project_id" {
  type = string
}

variable "region" {
  type = string
}

variable "name_prefix" {
  type = string
}

variable "image" {
  type        = string
  description = "Fully-qualified Artifact Registry image (…/consumers:<tag>)."
}

variable "service_account_email" {
  type = string
}

variable "network_id" {
  type        = string
  description = "VPC network self-link for Direct VPC egress."
}

variable "subnet_id" {
  type        = string
  description = "Subnet self-link the Cloud Run instances attach to (Direct VPC egress)."
}

variable "consumer_count" {
  type        = number
  description = "Pinned always-on instance count (default 1; >1 is an app concern)."
  default     = 1
}

variable "cpu" {
  type    = string
  default = "1"
}

variable "memory" {
  type    = string
  default = "512Mi"
}

variable "health_path" {
  type    = string
  default = "/health"
}

variable "plain_env" {
  type    = map(string)
  default = {}
}

variable "secret_env" {
  type    = map(string)
  default = {}
}
