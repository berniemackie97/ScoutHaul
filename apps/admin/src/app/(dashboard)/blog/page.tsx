"use client";

import { Plus, Edit, Trash2, Eye } from "lucide-react";
import { Button, Card, Badge, formatDate } from "@overhaul/ui";
import { trpc } from "@/lib/trpc";

export default function BlogAdminPage() {
  const { data: posts, isLoading } = trpc.blog.listAll.useQuery();

  return (
    <div>
      <div className="flex items-center justify-between mb-6">
        <div>
          <h1 className="text-2xl font-bold">Blog Posts</h1>
          <p className="text-sm text-muted-foreground">Manage content for SEO and customer engagement</p>
        </div>
        <Button><Plus className="h-4 w-4" /> New Post</Button>
      </div>

      <Card>
        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="border-b bg-muted/50">
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Title</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Author</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Status</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Tags</th>
                <th className="text-left text-xs font-medium text-muted-foreground p-3">Published</th>
                <th className="text-center text-xs font-medium text-muted-foreground p-3">Actions</th>
              </tr>
            </thead>
            <tbody>
              {isLoading ? (
                Array.from({ length: 3 }).map((_, i) => (
                  <tr key={i} className="border-b"><td colSpan={6} className="p-3"><div className="h-8 bg-muted rounded animate-pulse" /></td></tr>
                ))
              ) : posts?.length === 0 ? (
                <tr><td colSpan={6} className="p-8 text-center text-muted-foreground">No blog posts yet</td></tr>
              ) : (
                posts?.map((post) => (
                  <tr key={post.id} className="border-b hover:bg-muted/30">
                    <td className="p-3 text-sm font-medium max-w-[300px] truncate">{post.title}</td>
                    <td className="p-3 text-sm text-muted-foreground">{post.authorName}</td>
                    <td className="p-3">
                      <Badge variant={post.isPublished ? "success" : "secondary"}>
                        {post.isPublished ? "Published" : "Draft"}
                      </Badge>
                    </td>
                    <td className="p-3">
                      <div className="flex gap-1">
                        {post.tags.slice(0, 2).map((tag) => (
                          <Badge key={tag} variant="outline" className="text-xs">{tag}</Badge>
                        ))}
                      </div>
                    </td>
                    <td className="p-3 text-sm text-muted-foreground">
                      {post.publishedAt ? formatDate(post.publishedAt) : "-"}
                    </td>
                    <td className="p-3 text-center">
                      <div className="flex gap-1 justify-center">
                        <Button variant="ghost" size="icon" className="h-8 w-8"><Eye className="h-4 w-4" /></Button>
                        <Button variant="ghost" size="icon" className="h-8 w-8"><Edit className="h-4 w-4" /></Button>
                        <Button variant="ghost" size="icon" className="h-8 w-8 text-red-500"><Trash2 className="h-4 w-4" /></Button>
                      </div>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </Card>
    </div>
  );
}
