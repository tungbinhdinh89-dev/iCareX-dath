import { useRef, useState } from "react"
import {
  Alert,
  Box,
  Button,
  DialogActions,
  DialogContent,
  MenuItem,
  TextField,
  Typography,
} from "@mui/material"
import { Controller, useFieldArray, useForm } from "react-hook-form"
import { zodResolver } from "@hookform/resolvers/zod"
import { z } from "zod"
import { api } from "@/lib/apis/iCareXApi"
import { money, odoOf, odometerUnitLabel, today } from "@/lib/models/garage"
import { recordRequestKey, type RecordCommand } from "@/lib/models/recordRequest"
import { useGarage, type ModalState } from "@/lib/state/store"

const itemSchema = z.object({
  type: z.enum(["Repair", "Maintenance"]),
  title: z
    .string()
    .trim()
    .min(2, "Nhập nội dung từ 2 ký tự.")
    .max(100, "Tối đa 100 ký tự."),
  cost: z
    .number()
    .int("Tiền VNĐ phải là số nguyên.")
    .min(0, "Chi phí không được âm.")
    .max(10_000_000_000),
})
const schema = z.object({
  vehicleId: z.string().min(1, "Chọn phương tiện."),
  date: z
    .string()
    .regex(/^\d{4}-\d{2}-\d{2}$/, "Chọn ngày hợp lệ.")
    .refine((value) => value <= today(), "Ngày không được ở tương lai."),
  odo: z
    .number()
    .min(0)
    .max(9_999_999_999.99)
    .refine(
      (x) => Math.abs(x * 100 - Math.round(x * 100)) < 0.0001,
      "ODO tối đa 2 số lẻ.",
    ),
  cost: z
    .number()
    .int("Tiền VNĐ phải là số nguyên.")
    .min(0)
    .max(10_000_000_000),
  liters: z
    .number()
    .min(0)
    .max(500)
    .refine(
      (x) => Math.abs(x * 1000 - Math.round(x * 1000)) < 0.0001,
      "Số lít tối đa 3 số lẻ.",
    ),
  note: z.string().max(500),
  items: z.array(itemSchema).min(1).max(50),
})
type Fields = z.infer<typeof schema>

export default function CostRecordForm({
  modal,
  onPendingChange,
}: {
  modal: Extract<ModalState, { type: "entry" }>
  onPendingChange: (pending: boolean) => void
}) {
  const { data, can, refetch, notify, setModal } = useGarage()
  const fuel = modal.kind === "fuel"
  const [error, setError] = useState("")
  const command = useRef<RecordCommand | null>(null)
  const permission = `${fuel ? "fuel" : "maintenance"}.${
    modal.entry ? "update" : "create"
  }`
  const available =
    data?.vehicles.filter((v) => can(permission, v.id, modal.entry)) ?? []
  const initialId =
    modal.entry?.vehicleId ?? modal.vehicleId ?? available[0]?.id ?? ""
  const initialVehicle = data?.vehicles.find((v) => v.id === initialId)
  const {
    register,
    control,
    watch,
    handleSubmit,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<Fields>({
    resolver: zodResolver(schema),
    defaultValues: {
      vehicleId: initialId,
      date: modal.entry?.date ?? today(),
      odo:
        modal.entry?.odo ??
        (initialVehicle && data ? odoOf(initialVehicle, data.entries) : 0),
      cost: modal.entry?.cost ?? 0,
      liters: modal.entry?.liters ?? 0,
      note: modal.entry?.note ?? "",
      items: modal.entry?.items?.map((x) => ({
        type: x.type as "Repair" | "Maintenance",
        title: x.title,
        cost: x.cost,
      })) ?? [{ type: "Maintenance", title: "Bảo dưỡng", cost: 0 }],
    },
  })
  const { fields, append, remove } = useFieldArray({ control, name: "items" })
  const values = watch()
  const total = fuel
    ? values.cost
    : values.items.reduce(
        (sum, x) => sum + (Number.isFinite(x.cost) ? x.cost : 0),
        0,
      )
  const submit = handleSubmit(async (values) => {
    setError("")
    if (!can(permission, values.vehicleId, modal.entry)) {
      setError("Quyền thao tác đã thay đổi.")
      return
    }
    if (total <= 0 || total > 10_000_000_000 || (fuel && values.liters <= 0)) {
      setError(
        "Tổng chi phí phải lớn hơn 0 và tối đa 10 tỷ VNĐ; lượng nhiên liệu phải lớn hơn 0.",
      )
      return
    }
    const common = {
      date: values.date,
      odo: values.odo,
      note: values.note,
      version: modal.entry?.version ?? null,
    }
    const body = fuel
      ? { ...common, cost: values.cost, liters: values.liters }
      : {
          ...common,
          items: values.items.map((x) => ({
            type: x.type,
            title: x.title,
            cost: x.cost,
            planId: null,
          })),
        }
    command.current = recordRequestKey(command.current, {
      vehicleId: values.vehicleId,
      id: modal.entry?.id,
      kind: modal.kind,
      body,
    })
    onPendingChange(true)
    try {
      if ("liters" in body)
        await api.saveFuel(values.vehicleId, modal.entry?.id, {
          ...body,
          requestId: command.current.key,
        })
      else
        await api.saveService(values.vehicleId, modal.entry?.id, {
          ...body,
          requestId: command.current.key,
        })
      await refetch()
      notify("Đã lưu bản ghi và cập nhật chi phí, ODO.")
      setModal(null)
    } catch (e) {
      setError(
        e instanceof Error
          ? e.message
          : "Không thể lưu bản ghi. Vui lòng thử lại.",
      )
    } finally {
      onPendingChange(false)
    }
  })
  return (
    <Box component="form" onSubmit={submit} noValidate>
      <DialogContent>
        {error && (
          <Alert severity="error" sx={{ mb: 2 }}>
            {error}
          </Alert>
        )}
        <Box
          component="fieldset"
          disabled={isSubmitting}
          sx={{ border: 0, p: 0, m: 0 }}
        >
          <Box className="form-grid">
            <Controller
              name="vehicleId"
              control={control}
              render={({ field }) => (
                <TextField
                  {...field}
                  select
                  label="Phương tiện"
                  className="full"
                  disabled={!!modal.entry}
                  error={!!errors.vehicleId}
                  helperText={errors.vehicleId?.message}
                  onChange={(e) => {
                    field.onChange(e)
                    const vehicle = data?.vehicles.find(
                      (v) => v.id === e.target.value,
                    )
                    if (vehicle && data)
                      setValue("odo", odoOf(vehicle, data.entries))
                  }}
                >
                  {available.map((v) => (
                    <MenuItem value={v.id} key={v.id}>
                      {v.name} · {v.plate}
                    </MenuItem>
                  ))}
                </TextField>
              )}
            />
            <TextField
              label="Ngày ghi nhận"
              type="date"
              {...register("date")}
              slotProps={{ inputLabel: { shrink: true } }}
              error={!!errors.date}
              helperText={errors.date?.message}
            />
            <TextField
              label={`ODO (${odometerUnitLabel(data?.vehicles.find((v) => v.id === values.vehicleId))})`}
              type="number"
              {...register("odo", { valueAsNumber: true })}
              error={!!errors.odo}
              helperText={errors.odo?.message}
            />
            {fuel && (
              <>
                <TextField
                  label="Chi phí (VNĐ)"
                  type="number"
                  {...register("cost", { valueAsNumber: true })}
                  error={!!errors.cost}
                  helperText={errors.cost?.message}
                />
                <TextField
                  label="Lượng nhiên liệu (lít)"
                  type="number"
                  {...register("liters", { valueAsNumber: true })}
                  error={!!errors.liters}
                  helperText={errors.liters?.message}
                  slotProps={{ htmlInput: { step: "0.001" } }}
                />
              </>
            )}
            {!fuel && (
              <Box className="full">
                <Typography variant="subtitle2" sx={{ mb: 2 }}>
                  Hạng mục dịch vụ
                </Typography>
                {fields.map((item, index) => (
                  <Box key={item.id} sx={{ display: "grid", gap: 2, mb: 3 }}>
                    <Controller
                      name={`items.${index}.type`}
                      control={control}
                      render={({ field }) => (
                        <TextField
                          {...field}
                          select
                          label={`Loại hạng mục ${index + 1}`}
                        >
                          <MenuItem value="Repair">Sửa chữa</MenuItem>
                          <MenuItem value="Maintenance">Bảo dưỡng</MenuItem>
                        </TextField>
                      )}
                    />
                    <TextField
                      label={`Nội dung ${index + 1}`}
                      {...register(`items.${index}.title`)}
                      error={!!errors.items?.[index]?.title}
                      helperText={errors.items?.[index]?.title?.message}
                    />
                    <TextField
                      label={`Chi phí ${index + 1} (VNĐ)`}
                      type="number"
                      {...register(`items.${index}.cost`, {
                        valueAsNumber: true,
                      })}
                      error={!!errors.items?.[index]?.cost}
                      helperText={errors.items?.[index]?.cost?.message}
                    />
                    <Button
                      color="error"
                      disabled={fields.length === 1}
                      onClick={() => remove(index)}
                    >
                      Xóa hạng mục {index + 1}
                    </Button>
                  </Box>
                ))}
                <Button
                  disabled={fields.length >= 50}
                  onClick={() =>
                    append({ type: "Maintenance", title: "", cost: 0 })
                  }
                >
                  Thêm hạng mục
                </Button>
                <Typography sx={{ mt: 2 }}>
                  Tổng chi phí: {money(total)}
                </Typography>
              </Box>
            )}
            <TextField
              label="Ghi chú"
              className="full"
              multiline
              minRows={2}
              {...register("note")}
              error={!!errors.note}
              helperText={errors.note?.message}
            />
          </Box>
        </Box>
      </DialogContent>
      <DialogActions>
        <Button disabled={isSubmitting} onClick={() => setModal(null)}>
          Hủy
        </Button>
        <Button
          type="submit"
          variant="contained"
          disabled={isSubmitting || !available.length}
        >
          {isSubmitting ? "Đang lưu..." : "Lưu bản ghi"}
        </Button>
      </DialogActions>
    </Box>
  )
}
