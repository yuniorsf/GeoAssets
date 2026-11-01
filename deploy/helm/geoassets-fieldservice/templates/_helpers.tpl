{{- define "geoassets-fieldservice.fullname" -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "geoassets-fieldservice.labels" -}}
app.kubernetes.io/name: geoassets-fieldservice
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
helm.sh/chart: {{ .Chart.Name }}-{{ .Chart.Version }}
multitenancy.geoassets.io/mode: {{ .Values.multiTenancy.mode }}
{{- if .Values.multiTenancy.tenantName }}
multitenancy.geoassets.io/tenant: {{ .Values.multiTenancy.tenantName }}
{{- end }}
{{- end -}}

{{- define "geoassets-fieldservice.selectorLabels" -}}
app.kubernetes.io/name: geoassets-fieldservice
app.kubernetes.io/instance: {{ .Release.Name }}
{{- end -}}

{{- define "geoassets-fieldservice.secretName" -}}
{{- if .Values.existingSecretName -}}
{{ .Values.existingSecretName }}
{{- else -}}
{{ include "geoassets-fieldservice.fullname" . }}-secret
{{- end -}}
{{- end -}}
