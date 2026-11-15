import { CheckCircleOutlined, ClockCircleOutlined } from '@ant-design/icons'
import { zodResolver } from '@hookform/resolvers/zod'
import {
  Alert,
  App,
  Button,
  Empty,
  Flex,
  Form,
  Input,
  Modal,
  Tag,
  Timeline,
  Typography,
} from 'antd'
import { useState } from 'react'
import { Controller, useForm } from 'react-hook-form'
import { z } from 'zod'
import type { Milestone, Project } from '@/features/projects/api/projectsApi'
import { useCreateMilestone, useUpdateMilestone } from '@/features/projects/hooks/useProjectDetail'
import { applyServerErrors, parseProblem, problemMessage } from '@/shared/api/problemDetails'

interface MilestoneTimelineProps {
  project: Project
  milestones: Milestone[]
  loading: boolean
  /** Manager proyek ini (atau admin tenant) dengan `project.update`. */
  canManage: boolean
}

const milestoneSchema = z.object({
  name: z.string().trim().min(1, 'Nama wajib diisi').max(200, 'Nama maksimal 200 karakter'),
  description: z.string().trim().max(2000, 'Deskripsi maksimal 2000 karakter'),
  dueDate: z.string().min(1, 'Tanggal jatuh tempo wajib diisi'),
})

type MilestoneValues = z.infer<typeof milestoneSchema>

/** Milestone urut tanggal jatuh tempo; manager bisa menambah dan menandai selesai. */
export function MilestoneTimeline({
  project,
  milestones,
  loading,
  canManage,
}: MilestoneTimelineProps) {
  const { message } = App.useApp()
  const update = useUpdateMilestone(project.id)
  const [creating, setCreating] = useState(false)
  const editable = canManage && project.status !== 'archived'

  const toggle = (milestone: Milestone) =>
    update.mutate(
      { milestone, status: milestone.status === 'open' ? 'completed' : 'open' },
      {
        onError: (error) => {
          const problem = parseProblem(error)
          message.error(
            problem.status === 409
              ? 'Milestone sudah diubah orang lain. Data terbaru sudah dimuat; coba lagi.'
              : problemMessage(problem),
          )
        },
      },
    )

  const range =
    project.startDate || project.endDate
      ? `Tanggal milestone harus di antara ${project.startDate ?? 'tanpa batas'} dan ${project.endDate ?? 'tanpa batas'}.`
      : null

  return (
    <>
      {editable && (
        <Flex justify="end" style={{ marginBottom: 16 }}>
          <Button type="primary" onClick={() => setCreating(true)}>
            Milestone baru
          </Button>
        </Flex>
      )}
      {!loading && milestones.length === 0 ? (
        <Empty description="Belum ada milestone" />
      ) : (
        <Timeline
          items={milestones.map((m) => ({
            key: m.id,
            color: m.status === 'completed' ? 'green' : 'blue',
            dot: m.status === 'completed' ? <CheckCircleOutlined /> : <ClockCircleOutlined />,
            children: (
              <Flex justify="space-between" align="start" gap={12} wrap>
                <div>
                  <Typography.Text strong>{m.name}</Typography.Text>{' '}
                  <Tag color={m.status === 'completed' ? 'green' : 'blue'}>
                    {m.status === 'completed' ? 'Selesai' : 'Berjalan'}
                  </Tag>
                  <div>
                    <Typography.Text type="secondary">Jatuh tempo {m.dueDate}</Typography.Text>
                  </div>
                  {m.description && <Typography.Paragraph>{m.description}</Typography.Paragraph>}
                </div>
                {editable && (
                  <Button
                    size="small"
                    loading={update.isPending && update.variables.milestone.id === m.id}
                    onClick={() => toggle(m)}
                  >
                    {m.status === 'open' ? 'Tandai selesai' : 'Buka lagi'}
                  </Button>
                )}
              </Flex>
            ),
          }))}
        />
      )}
      {creating && (
        <CreateMilestoneModal
          projectId={project.id}
          hint={range}
          onClose={() => setCreating(false)}
        />
      )}
    </>
  )
}

function CreateMilestoneModal({
  projectId,
  hint,
  onClose,
}: {
  projectId: string
  hint: string | null
  onClose: () => void
}) {
  const { message } = App.useApp()
  const create = useCreateMilestone(projectId)
  const [formError, setFormError] = useState<string | null>(null)
  const {
    control,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<MilestoneValues>({
    resolver: zodResolver(milestoneSchema),
    defaultValues: { name: '', description: '', dueDate: '' },
  })

  const submit = async (values: MilestoneValues) => {
    setFormError(null)
    try {
      await create.mutateAsync({
        name: values.name,
        description: values.description || null,
        dueDate: values.dueDate,
      })
      message.success('Milestone dibuat.')
      onClose()
    } catch (e) {
      // 422 (di luar rentang tanggal proyek) tampil sebagai pesan form.
      setFormError(applyServerErrors(e, setError, ['name', 'description', 'dueDate'] as const))
    }
  }

  return (
    <Modal
      title="Milestone baru"
      open
      onCancel={onClose}
      footer={null}
      closable={!isSubmitting}
      maskClosable={!isSubmitting}
    >
      <Form layout="vertical" onFinish={handleSubmit(submit)} noValidate>
        {formError && (
          <Alert
            type="error"
            showIcon
            role="alert"
            style={{ marginBottom: 16 }}
            message={formError}
          />
        )}
        <Form.Item
          label="Nama"
          htmlFor="milestone-name"
          required
          validateStatus={errors.name ? 'error' : ''}
          help={errors.name?.message}
        >
          <Controller
            name="name"
            control={control}
            render={({ field }) => <Input {...field} id="milestone-name" />}
          />
        </Form.Item>
        <Form.Item
          label="Tanggal jatuh tempo"
          htmlFor="milestone-dueDate"
          required
          validateStatus={errors.dueDate ? 'error' : ''}
          help={errors.dueDate?.message ?? hint}
        >
          <Controller
            name="dueDate"
            control={control}
            render={({ field }) => <Input {...field} id="milestone-dueDate" type="date" />}
          />
        </Form.Item>
        <Form.Item
          label="Deskripsi"
          htmlFor="milestone-description"
          validateStatus={errors.description ? 'error' : ''}
          help={errors.description?.message}
        >
          <Controller
            name="description"
            control={control}
            render={({ field }) => (
              <Input.TextArea {...field} id="milestone-description" rows={3} />
            )}
          />
        </Form.Item>
        <Flex justify="end" gap={8}>
          <Button onClick={onClose} disabled={isSubmitting}>
            Batal
          </Button>
          <Button type="primary" htmlType="submit" loading={isSubmitting}>
            Buat milestone
          </Button>
        </Flex>
      </Form>
    </Modal>
  )
}
