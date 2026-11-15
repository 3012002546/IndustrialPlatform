<script setup lang="ts">
import { useReportCopy } from './copy'
const format = defineModel<string>({ required: true })
defineProps<{ disabled?: boolean }>()
const t = useReportCopy()
</script>
<template>
  <el-select v-model="format" :disabled="disabled" :aria-label="t('output')" style="width: 190px">
    <el-option label="PDF" value="Pdf" /><el-option label="HTML" value="Html" />
    <el-option label="Excel (.xlsx)" value="Xlsx" /><el-option
      label="Excel 97–2003 (.xls)"
      value="Xls"
    />
    <el-option label="Word (.docx)" value="Docx" /><el-option label="RTF" value="Rtf" />
    <el-option label="MHT" value="Mht" /><el-option :label="t('textExport')" value="Text" />
    <el-option label="CSV" value="Csv" /><el-option :label="t('pngExport')" value="Png" />
    <el-option :label="t('jpegExport')" value="Jpeg" />
  </el-select>
</template>
