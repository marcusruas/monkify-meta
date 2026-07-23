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
  description = "Fully-qualified Artifact Registry image (…/api:<tag>)."
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

variable "min_instances" {
  type    = number
  default = 1
}

variable "max_instances" {
  type    = number
  default = 10
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
  type        = map(string)
  description = "Non-secret env vars (name -> value)."
  default     = {}
}

variable "secret_env" {
  type        = map(string)
  description = "Secret env vars (env var name -> Secret Manager secret id)."
  default     = {}
}

variable "allow_public_invoker" {
  type    = bool
  default = true
}
