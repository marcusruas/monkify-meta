variable "project_id" {
  type = string
}

variable "region" {
  type = string
}

variable "name_prefix" {
  type        = string
  description = "Prefix for network resource names (e.g. spacestroker)."
}

variable "subnet_cidr" {
  type        = string
  description = "Primary subnet CIDR."
  default     = "10.10.0.0/24"
}

variable "connector_cidr" {
  type        = string
  description = "Serverless VPC Access connector /28 CIDR (must not overlap the subnet)."
  default     = "10.10.8.0/28"
}
